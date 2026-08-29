using RISL.Application.Import;

namespace RISL.Tests.Import;

public class DelimitedTextParserTests
{
    [Theory]
    [InlineData("слово;описание;видео", ';')]
    [InlineData("слово,описание,видео", ',')]
    [InlineData("слово\tописание\tвидео", '\t')]
    public void DetectDelimiter_ОпределяетРазделительПоЗаголовку(string header, char expected)
    {
        Assert.Equal(expected, DelimitedTextParser.DetectDelimiter(header));
    }

    [Fact]
    public void DetectDelimiter_НеСчитаетРазделителиВнутриКавычек()
    {
        Assert.Equal(';', DelimitedTextParser.DetectDelimiter("\"первое, второе\";третье"));
    }

    [Fact]
    public void Parse_РазбираетПростыеСтроки()
    {
        var rows = DelimitedTextParser.Parse("слово;описание\nкошка;животное", ';');

        Assert.Equal(2, rows.Count);
        Assert.Equal(["кошка", "животное"], rows[1].Fields);
    }

    [Fact]
    public void Parse_СохраняетРазделительВнутриКавычек()
    {
        var rows = DelimitedTextParser.Parse("кошка;\"домашнее животное, родственник тигра\"", ';');

        Assert.Equal("домашнее животное, родственник тигра", rows[0].Fields[1]);
    }

    [Fact]
    public void Parse_ПониматУдвоеннуюКавычкуКакСимвол()
    {
        var rows = DelimitedTextParser.Parse("кошка;\"жест \"\"мягкий\"\"\"", ';');

        Assert.Equal("жест \"мягкий\"", rows[0].Fields[1]);
    }

    [Fact]
    public void Parse_ДопускаетПереносСтрокиВнутриПоля()
    {
        var rows = DelimitedTextParser.Parse("кошка;\"первая строка\nвторая строка\"\nсобака;животное", ';');

        Assert.Equal(2, rows.Count);
        Assert.Equal("первая строка\nвторая строка", rows[0].Fields[1]);
        Assert.Equal("собака", rows[1].Fields[0]);
    }

    [Fact]
    public void Parse_НумеруетСтрокиПоИсходномуФайлу()
    {
        var rows = DelimitedTextParser.Parse("слово;описание\r\nкошка;животное\r\n\r\nсобака;животное", ';');

        Assert.Equal(1, rows[0].LineNumber);
        Assert.Equal(2, rows[1].LineNumber);
        // Пустая третья строка пропущена, но нумерация не сбилась.
        Assert.Equal(4, rows[2].LineNumber);
    }

    [Fact]
    public void Parse_ПропускаетBomВНачалеФайла()
    {
        var rows = DelimitedTextParser.Parse("﻿слово;описание", ';');

        Assert.Equal("слово", rows[0].Fields[0]);
    }

    [Fact]
    public void Parse_ОбрабатываетПустоеСодержимое()
    {
        Assert.Empty(DelimitedTextParser.Parse(string.Empty, ';'));
    }
}
