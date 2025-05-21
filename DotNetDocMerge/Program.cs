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
                                .Replace("{", "&lbrace;")
                                .Replace("}", "&rbrace;")
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
        // First, find all code blocks and store them
        var codeBlocks = new List<string>();
        var regex = new Regex(@"<code>(.*?)</code>", RegexOptions.Singleline);
        text = regex.Replace(text, match =>
        {
            string codeContent = match.Groups[1].Value;
            // Replace special characters that might cause issues in HTML/JavaScript
            codeContent = codeContent
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;")
                .Replace("`", "&#96;");
            
            codeBlocks.Add(codeContent);
            return $"<code>{codeBlocks.Count - 1}</code>";
        });

        // Now replace the code blocks with the proper HTML structure
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            text = text.Replace($"<code>{i}</code>", $"""
<div class="code-snippet-wrapper card">
    <div class="copy-button-wrapper">
        <app-copy-button (onClick)="copyCodeSnippet($event)"></app-copy-button>
    </div>
    <pre><code language="csharp" [highlight]="`{codeBlocks[i]}`">
    </code></pre>
</div>
"""
            );
        }

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
        
        string formattedTitle = Regex.Replace(folderName, "([A-Z])", " $1").Trim();
        
        sb.AppendLine($"""
<div>
    <h1 class="gradient-title">Spiderly {formattedTitle}</h1>
    
    <div class="toc card">
        <h2>Table of Contents</h2>
        <ul>
""");

        var sortedItems = items.OrderBy(item => item.Title).ToList();
        
        // Generate table of contents with routerLink
        foreach (DocumentationItem item in sortedItems)
        {
            string anchor = item.Title.ToKebabCase();
            sb.AppendLine($"""
            <li>
                <a [routerLink]="['/docs', '{folderName.ToKebabCase()}']" 
                    [fragment]="'{anchor}'"
                    title="Go to {item.Title}">
                    {item.Title}
                </a>
            </li>
""");
        }

        sb.AppendLine("""
        </ul>
    </div>

""");

        foreach (DocumentationItem item in sortedItems)
        {
            string anchor = item.Title.ToKebabCase();
            sb.AppendLine($"""
    <section id="{anchor}" class="doc-section">
        <div class="card">
            <h2>{item.Title}</h2>
            <div class="namespace">Namespace: {item.Namespace}</div>
""");

            foreach (string summary in item.Summaries)
            {
                sb.AppendLine($"""
            <div class="summary">
{summary}
            </div>
""");
            }

            sb.AppendLine("""
        </div>
    </section>
""");
        }
        
            sb.AppendLine("""
</div>
""");
        return sb.ToString();
    }
}

class DocumentationItem
{
    public required string Title { get; init; }
    public required string Namespace { get; init; }
    public required List<string> Summaries { get; init; }
}

