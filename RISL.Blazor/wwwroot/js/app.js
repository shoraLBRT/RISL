// Весь клиентский код словаря. Страницы рендерит сервер, поэтому здесь только то,
// что в принципе невозможно сделать разметкой: скорость воспроизведения, повтор,
// избранное в браузере и отправка поиска по мере ввода.
//
// Всё построено как прогрессивное улучшение: при выключенном JavaScript поиск
// работает кнопкой, видео — штатными контролами браузера, а избранное просто скрыто.

(function () {
    'use strict';

    var FAVORITES_KEY = 'risl.favorites';
    var SPEED_KEY = 'risl.playbackRate';
    var LOOP_KEY = 'risl.loop';
    var SEARCH_DELAY_MS = 250;

    // Плавная навигация сохраняет совпадающие узлы DOM, поэтому после неё часть
    // элементов остаётся прежней. Метка не даёт навесить обработчик второй раз.
    function claim(element, key) {
        var attribute = 'data-bound-' + key;
        if (element.hasAttribute(attribute)) {
            return false;
        }

        element.setAttribute(attribute, '');
        return true;
    }

    // ---------- Избранное ----------

    function readFavorites() {
        try {
            var raw = window.localStorage.getItem(FAVORITES_KEY);
            var parsed = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed.filter(function (id) { return Number.isInteger(id); }) : [];
        } catch (error) {
            // Приватный режим или испорченное хранилище — работаем без избранного.
            return [];
        }
    }

    function writeFavorites(ids) {
        try {
            window.localStorage.setItem(FAVORITES_KEY, JSON.stringify(ids));
        } catch (error) {
            /* места нет или хранилище запрещено — молча продолжаем */
        }
    }

    function toggleFavorite(id) {
        var ids = readFavorites();
        var index = ids.indexOf(id);

        if (index >= 0) {
            ids.splice(index, 1);
        } else {
            ids.push(id);
        }

        writeFavorites(ids);
        return index < 0;
    }

    function paintFavorite(button, isFavorite) {
        button.setAttribute('aria-pressed', isFavorite ? 'true' : 'false');
        button.textContent = isFavorite ? '★' : '☆';

        var label = isFavorite ? 'Убрать из избранного' : 'Добавить в избранное';
        button.setAttribute('aria-label', label);
        button.setAttribute('title', label);
    }

    function initFavorites(root) {
        var ids = readFavorites();

        root.querySelectorAll('[data-favorite-id]').forEach(function (button) {
            var id = parseInt(button.getAttribute('data-favorite-id'), 10);
            paintFavorite(button, ids.indexOf(id) >= 0);

            if (!claim(button, 'favorite')) {
                return;
            }

            button.addEventListener('click', function (event) {
                event.preventDefault();
                event.stopPropagation();
                paintFavorite(button, toggleFavorite(id));
            });
        });

        initFavoritesPage(root);
    }

    // Страница избранного не может знать список на сервере: он живёт в браузере.
    // Поэтому при открытии отправляем идентификаторы обычной формой, и сервер
    // отрисовывает те же карточки, что и на главной.
    function initFavoritesPage(root) {
        var pending = root.querySelector('[data-favorites-pending]');
        if (!pending) {
            return;
        }

        var form = root.querySelector('[data-favorites-form]');
        var field = form && form.querySelector('[data-favorites-ids]');
        if (!form || !field) {
            return;
        }

        var ids = readFavorites();
        if (ids.length === 0) {
            pending.removeAttribute('hidden');
            return;
        }

        field.value = ids.join(',');
        form.submit();
    }

    // ---------- Плеер ----------

    function readSpeed() {
        var stored = parseFloat(window.localStorage.getItem(SPEED_KEY));
        return stored > 0 ? stored : 1;
    }

    function readLoop() {
        return window.localStorage.getItem(LOOP_KEY) === '1';
    }

    function applyPlayerSettings(root) {
        var speed = readSpeed();
        var loop = readLoop();

        root.querySelectorAll('video').forEach(function (video) {
            video.playbackRate = speed;
            video.loop = loop;
        });

        root.querySelectorAll('[data-speed]').forEach(function (button) {
            button.setAttribute('aria-pressed', parseFloat(button.getAttribute('data-speed')) === speed ? 'true' : 'false');
        });

        root.querySelectorAll('[data-loop-toggle]').forEach(function (button) {
            button.setAttribute('aria-pressed', loop ? 'true' : 'false');
        });
    }

    function initPlayer(root) {
        if (!root.querySelector('video')) {
            return;
        }

        root.querySelectorAll('[data-speed]').forEach(function (button) {
            if (!claim(button, 'speed')) {
                return;
            }

            button.addEventListener('click', function () {
                // Замедление — основной инструмент разбора жеста, поэтому выбор
                // запоминается и переносится на все следующие ролики.
                window.localStorage.setItem(SPEED_KEY, button.getAttribute('data-speed'));
                applyPlayerSettings(document);
            });
        });

        root.querySelectorAll('[data-loop-toggle]').forEach(function (button) {
            if (!claim(button, 'loop')) {
                return;
            }

            button.addEventListener('click', function () {
                window.localStorage.setItem(LOOP_KEY, readLoop() ? '0' : '1');
                applyPlayerSettings(document);
            });
        });

        // Браузер сбрасывает playbackRate при загрузке нового источника.
        root.querySelectorAll('video').forEach(function (video) {
            if (!claim(video, 'video')) {
                return;
            }

            video.addEventListener('loadedmetadata', function () {
                video.playbackRate = readSpeed();
                video.loop = readLoop();
            });
        });

        applyPlayerSettings(root);
    }

    // ---------- Поиск по мере ввода ----------

    // Подменяем только блок результатов: запрашиваем ту же страницу обычным GET
    // и переносим из ответа содержимое [data-results]. Поле ввода при этом никуда
    // не девается, поэтому не теряются ни фокус, ни позиция каретки.
    function swapResults(url, addToHistory) {
        var target = document.querySelector('[data-results]');
        if (!target) {
            window.location.assign(url);
            return;
        }

        window.fetch(url, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }

                return response.text();
            })
            .then(function (html) {
                var parsed = new DOMParser().parseFromString(html, 'text/html');
                var fresh = parsed.querySelector('[data-results]');
                if (!fresh) {
                    throw new Error('в ответе нет блока результатов');
                }

                target.innerHTML = fresh.innerHTML;
                document.title = parsed.title;

                if (addToHistory) {
                    window.history.pushState({ results: true }, '', url);
                }

                // Карточки пришли новые — им нужны обработчики и состояние звёздочки.
                initFavorites(target);
            })
            .catch(function () {
                // Сеть отвалилась или ответ неожиданный — уходим обычной навигацией,
                // чтобы пользователь увидел либо результат, либо честную ошибку браузера.
                window.location.assign(url);
            });
    }

    function urlOf(form) {
        var query = new URLSearchParams(new FormData(form));

        // Пустые параметры в адресе только мешают им же делиться.
        [...query.keys()].forEach(function (key) {
            if (!query.get(key)) {
                query.delete(key);
            }
        });

        var text = query.toString();
        return text ? form.action + '?' + text : form.action;
    }

    function initSearch(root) {
        root.querySelectorAll('[data-search-form]').forEach(function (form) {
            if (!claim(form, 'search')) {
                return;
            }

            // Помечаем текущую запись истории: иначе возврат «назад» к самой первой
            // выдаче менял бы адрес, но оставлял на экране результаты прошлого запроса.
            if (!window.history.state || !window.history.state.results) {
                window.history.replaceState({ results: true }, '', window.location.href);
            }

            var input = form.querySelector('[data-search-input]');
            var timer = null;
            var lastSubmitted = input ? input.value : '';

            form.addEventListener('submit', function (event) {
                event.preventDefault();
                window.clearTimeout(timer);
                lastSubmitted = input ? input.value : '';
                swapResults(urlOf(form), true);
            });

            if (!input) {
                return;
            }

            input.addEventListener('input', function () {
                window.clearTimeout(timer);

                timer = window.setTimeout(function () {
                    if (input.value === lastSubmitted) {
                        return;
                    }

                    lastSubmitted = input.value;
                    swapResults(urlOf(form), true);
                }, SEARCH_DELAY_MS);
            });
        });
    }

    // Кнопка «назад» должна возвращать прошлую выдачу, а не просто менять адрес.
    window.addEventListener('popstate', function (event) {
        if (event.state && event.state.results && document.querySelector('[data-results]')) {
            swapResults(window.location.href, false);
        }
    });

    // ---------- Инициализация ----------

    function initialize() {
        document.body.classList.add('js-ready');

        initFavorites(document);
        initPlayer(document);
        initSearch(document);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }

})();
