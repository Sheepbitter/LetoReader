using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MarkedNet;
using HtmlAgilityPack;
using UglyToad.PdfPig;
using EpubSharp;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;
using Reader.Data.ProductExceptions;
using Reader.Data.Storage;
using Reader.Data.Reading;

namespace Reader.Modules.Product;

public class FileImporter
{
    private static Regex trimWhitespace = new(@"\s\s+", RegexOptions.Compiled);

    public static async Task<Tuple<ReaderState,string>> ExtractFromBrowserFiles(IReadOnlyList<IBrowserFile> files)
    {
        StringBuilder sb = new();
        int pageCount = 0;
        bool[] fileSupported = new bool[files.Count];

        foreach ((var file, int i) in files.Select((file, i) => (file, i)))
        {
            using (var ms = new MemoryStream())
            {
                await file.OpenReadStream(file.Size).CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);

                var fileBytes = ms.ToArray();

                if (file.ContentType == "text/plain")
                {
                    sb.Append(Encoding.UTF8.GetString(fileBytes));
                }
                else if (file.Name.EndsWith(".epub"))
                {
                    var epubResult = ExtractFromEpub(fileBytes);
                    sb.Append(epubResult.Item1);
                    pageCount = epubResult.Item2;
                }
                else if (file.Name.EndsWith(".md"))
                {
                    sb.Append(ExtractStringFromMdStr(Encoding.UTF8.GetString(fileBytes)));
                }
                else if (file.ContentType == "application/pdf")
                {
                    var result = ExtractStringFromPDF(fileBytes);
                    sb.Append(result.Item1);
                    pageCount = result.Item2;
                }
                else if (file.ContentType == "text/html")
                {
                    sb.Append(ExtractStringFromHTMLStr(Encoding.UTF8.GetString(fileBytes)));
                }
                else
                {
                    fileSupported[i] = false;
                    continue;
                }
                fileSupported[i] = true;
            }

            if (i < files.Count - 1)
            {
                sb.Append(Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine);
            }
        }

        if (fileSupported.All(x => !x))
        {
            throw new UnsupportedOperationException("Unsupported file type", "Supported file type are: " + ProductConstants.SupportedFileImports);
        }

        string title = string.Join(", ", files.Select(
           file => file.Name.Substring(0, file.Name.LastIndexOf('.'))
        ));

        string sourceDescription = "Upload of: " + string.Join(", ", files.Select(
           file => file.Name)
        );

        return new Tuple<ReaderState,string>(new ReaderState(title, sb.ToString(), ReaderStateSource.FileUpload, sourceDescription, pageCount: pageCount), sb.ToString());
    }

    public static Tuple<string, int> ExtractStringFromPDF(byte[] byteArr)
    {
        StringBuilder sb = new();
        int pageCount = 0;

        using (var document = PdfDocument.Open(byteArr))
        {
            foreach (var page in document.GetPages())
            {
                if (pageCount > 0)
                {
                    sb.Append("\f");
                }
                var text = ContentOrderTextExtractor.GetText(page, true);
                sb.Append(text);
                pageCount++;
            }
        }
        return new Tuple<string, int>(sb.ToString(), pageCount);
    }

    public static string ExtractStringFromHTMLStr(string htmlstr)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlstr);
        return doc.DocumentNode.InnerText;
    }

    public static Tuple<string, int> ExtractFromEpub(byte[] arr)
    {
        EpubBook book = EpubReader.Read(arr);
        StringBuilder sb = new();
        int pageCount = 0;

        foreach (var htmlFile in book.SpecialResources.HtmlInReadingOrder)
        {
            string plainText = trimWhitespace.Replace(ExtractStringFromHTMLStr(htmlFile.TextContent), " ").Trim();
            if (plainText.Length == 0)
                continue;

            if (pageCount > 0)
            {
                sb.Append("\f ");
            }
            sb.Append(plainText);
            pageCount++;
        }

        return new Tuple<string, int>(sb.ToString(), pageCount);
    }

    public static string ExtractStringFromMdStr(string mdStr)
    {
        return ExtractStringFromHTMLStr(new Marked().Parse(mdStr));
    }
}
