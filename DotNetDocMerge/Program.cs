using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CaseConverter;

class Program
{
    static async Task Main(string[] args)
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        Console.WriteLine($"Searching for C# files in: {currentDirectory}");

        string[] files = Directory.GetFiles(currentDirectory, "*.cs", SearchOption.AllDirectories);
        if (!files.Any())
        {
            Console.WriteLine("No C# files found in the current directory.");
            return;
        }

        List<DocumentationItem> documentationItems = new();
        foreach (string file in files)
        {
            string relativeFilePath = Path.GetRelativePath(currentDirectory, file);
            Console.WriteLine($"Processing: {relativeFilePath}");

            string sourceText = await File.ReadAllTextAsync(file);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText);
            SyntaxNode root = await tree.GetRootAsync();

            string title = Path.GetFileNameWithoutExtension(file);
            string namespaceName = GetNamespace(root);

            List<string> summaries = CollectSummaries(root);
            if (summaries.Any())
            {
                documentationItems.Add(new DocumentationItem
                {
                    Title = title,
                    Namespace = namespaceName,
                    Summaries = summaries
                });
            }
        }

        if (!documentationItems.Any())
        {
            Console.WriteLine("No documentation found in any of the C# files.");
            return;
        }

        string html = GenerateHtml(documentationItems, Path.GetFileName(currentDirectory));
        string outputPath = Path.Combine(currentDirectory, "documentation.html");
        await File.WriteAllTextAsync(outputPath, html);
        Console.WriteLine($"Documentation generated: {outputPath}");
    }

    static string GetNamespace(SyntaxNode root)
    {
        var fileScopedNamespace = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScopedNamespace != null)
        {
            return fileScopedNamespace.Name.ToString();
        }

        var blockNamespace = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (blockNamespace != null)
        {
            return blockNamespace.Name.ToString();
        }

        return "Global Namespace";
    }

    static List<string> CollectSummaries(SyntaxNode root)
    {
        List<string> summaries = new();
        
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            if (node is TypeDeclarationSyntax or MethodDeclarationSyntax or PropertyDeclarationSyntax)
            {
                DocumentationCommentTriviaSyntax? trivia = node.GetLeadingTrivia()
                    .Select(t => t.GetStructure())
                    .OfType<DocumentationCommentTriviaSyntax>()
                    .FirstOrDefault();

                if (trivia != null)
                {
                    XmlElementSyntax? summary = trivia.ChildNodes()
                        .OfType<XmlElementSyntax>()
                        .FirstOrDefault(x => x.StartTag.Name.ToString() == "summary");

                    if (summary != null)
                    {
                        string summaryText = summary.ToString();
                        if (!string.IsNullOrWhiteSpace(summaryText))
                        {
                            summaryText = summaryText
                                .Replace("/// ", "")
                                .Replace("///", "") 
                                .Replace("<summary>", "")
                                .Replace("</summary>", "")
                                .Replace("<b>", "<h3>")
                                .Replace("</b>", "</h3>")
                                .Replace("<i>", "<span class=\"code-block\">")
                                .Replace("</i>", "</span>")
                                .Trim();

                            summaryText = ProcessCodeBlocks(summaryText);

                            summaries.Add(summaryText);
                        }
                    }
                }
            }
        }

        return summaries;
    }

    static string ProcessCodeBlocks(string text)
    {
        text = text.Replace("<code>", "<pre class=\"card\"><button class=\"copy-button\" onclick=\"copyCode(this)\">Copy</button><code class=\"language-csharp\">").Replace("</code>", "</code></pre>");

        string[] lines = text.Split('\n');
        List<string> processedLines = new();
        List<string> codeBlockLines = new();
        bool insidePreTag = false;

        foreach (string line in lines)
        {
            string processedLine = line.TrimEnd();

            if (processedLine.Contains("<pre"))
            {
                insidePreTag = true;
                codeBlockLines.Clear();
                processedLines.Add(processedLine);
                continue;
            }

            if (insidePreTag && !processedLine.Contains("</pre>"))
            {
                codeBlockLines.Add(processedLine);
                continue;
            }

            if (processedLine.Contains("</pre>"))
            {
                insidePreTag = false;
                if (codeBlockLines.Any())
                {
                    // Find the minimum indentation level
                    int minIndent = codeBlockLines
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => l.Length - l.TrimStart().Length)
                        .Min();

                    // Remove exactly minIndent leading whitespace characters
                    foreach (string codeLine in codeBlockLines)
                    {
                        if (string.IsNullOrWhiteSpace(codeLine))
                        {
                            processedLines.Add(string.Empty);
                        }
                        else
                        {
                            string trimmedLine = codeLine.TrimStart();
                            int leadingSpaces = codeLine.Length - trimmedLine.Length;
                            int spacesToRemove = Math.Min(leadingSpaces, minIndent);
                            processedLines.Add(codeLine.Substring(spacesToRemove));
                        }
                    }
                }
                processedLines.Add(processedLine);
                continue;
            }

            if (!insidePreTag)
            {
                processedLines.Add(processedLine);
            }
        }

        return string.Join("\n", processedLines);
    }

    static string GenerateHtml(List<DocumentationItem> items, string folderName)
    {
        StringBuilder sb = new();
        
        // Start HTML document
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
     
        // Add custom CSS
        sb.AppendLine("""
    <style>
        .toc { margin-bottom: 18px; }
        h1 { margin-bottom: 30px; }
        h2 { margin-bottom: 12px; }
        h3 { margin-bottom: 9px; }
        .doc-section { 
            margin-bottom: 18px; 
            padding-top: 80px;
            margin-top: -80px; /* HACK: Because of header and toc goto */
        }
        pre { padding: 12px 18px 0 18px !important; margin: 0 !important; position: relative; }
        code { padding: 0 !important; background-color: transparent !important; }
        .namespace { color: var(--p-surface-500); font-size: 0.9em; margin-bottom: 15px; }
        .copy-button {
          position: absolute;
          top: 6px;
          right: 6px;
          padding: 4px 8px;
          background: var(--p-surface-500);
          border: none;
          border-radius: 4px;
          cursor: pointer;
          font-size: 12px;
          opacity: 0.7;
          transition: opacity 0.2s;
        }
        .copy-button:hover {
          opacity: 1;
        }
    </style>
    <script>
        function copyCode(button) {
            const pre = button.parentElement;
            const code = pre.querySelector('code');
            const text = code.textContent;
            
            navigator.clipboard.writeText(text).then(() => {
                const originalText = button.textContent;
                button.textContent = 'Copied!';
                
                setTimeout(() => {
                    button.textContent = originalText;
                }, 2000);
            });
        }
    </script>
""");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        string formattedTitle = Regex.Replace(folderName, "([A-Z])", " $1").Trim();
        sb.AppendLine($"<h1 class=\"gradient-title\">Spiderly {formattedTitle}</h1>");
        
        var sortedItems = items.OrderBy(item => item.Title).ToList();
        
        // Table of Contents
        sb.AppendLine("<div class=\"toc card\">");
        sb.AppendLine("  <h2>Table of Contents</h2>");
        sb.AppendLine("  <ul>");
        foreach (DocumentationItem item in sortedItems)
        {
            string anchor = item.Title.ToKebabCase();
            sb.AppendLine($"    <li><a href=\"docs/{folderName.ToKebabCase()}/#{anchor}\">{item.Title}</a></li>");
        }
        sb.AppendLine("  </ul>");
        sb.AppendLine("</div>");
        
        foreach (DocumentationItem item in sortedItems)
        {
            string anchor = item.Title.ToKebabCase();
            sb.AppendLine($"<section id=\"{anchor}\" class=\"doc-section\">");
            sb.AppendLine($"  <div class=\"card\">");
            sb.AppendLine($"    <h2>{item.Title}</h2>");
            sb.AppendLine($"    <div class=\"namespace\">Namespace: {item.Namespace}</div>");
            
            foreach (string summary in item.Summaries)
            {
                sb.AppendLine($"    <div class=\"summary\">{summary}</div>");
            }
            sb.AppendLine("  </div>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        
        return sb.ToString();
    }
}

class DocumentationItem
{
    public required string Title { get; init; }
    public required string Namespace { get; init; }
    public required List<string> Summaries { get; init; }
}

