using Microsoft.AspNetCore.Components;
using System.Text;
using System.Text.RegularExpressions;

namespace NetworkMonitorChat
{
    public partial class MarkdownRenderer
    {
        [Parameter]
        public string Content { get; set; }

        // Use compiled regex for better performance
        private static readonly Regex ImageRegex = new Regex(@"!\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);
        private static readonly Regex LinkTargetBlankRegex = new Regex(@"\[(.*?)\]\((.*?)\)\{_blank\}", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);
        private static readonly Regex AutoLinkRegex = new Regex(@"<(https?://[^\s]+)>", RegexOptions.Compiled);
        private static readonly Regex StrongRegex = new Regex(@"\*\*(.*?)\*\*|__(.*?)__", RegexOptions.Compiled);
        private static readonly Regex EmphasisRegex = new Regex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)|(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex StrikethroughRegex = new Regex(@"~~(.*?)~~", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"(`+)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex EscapeRegex = new Regex(@"\\([\\`*_{}\[\]()#+\-.!])", RegexOptions.Compiled);

        public static string ToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            // Normalize line endings
            markdown = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            
            var result = new StringBuilder();
            var lines = markdown.Split('\n');
            bool inCodeBlock = false;
            bool inUnorderedList = false;
            bool inOrderedList = false;
            bool inBlockquote = false;
            bool inTable = false;
            List<string> tableLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                // Handle code blocks first
                if (line.StartsWith("```"))
                {
                    if (inUnorderedList)
                    {
                        result.AppendLine("</ul>");
                        inUnorderedList = false;
                    }
                    if (inOrderedList)
                    {
                        result.AppendLine("</ol>");
                        inOrderedList = false;
                    }
                    if (inBlockquote)
                    {
                        result.AppendLine("</blockquote>");
                        inBlockquote = false;
                    }
                    
                    if (inCodeBlock)
                    {
                        result.AppendLine("</code></pre>");
                        inCodeBlock = false;
                    }
                    else
                    {
                        string language = line.Length > 3 ? line.Substring(3).Trim() : "";
                        result.AppendLine($"<pre><code class=\"language-{language}\">");
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    // Inside code block, output text as-is with HTML encoding
                    result.AppendLine(System.Web.HttpUtility.HtmlEncode(line));
                    continue;
                }

                // Check for blank lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Close any open blocks
                    if (inUnorderedList)
                    {
                        result.AppendLine("</ul>");
                        inUnorderedList = false;
                    }
                    if (inOrderedList)
                    {
                        result.AppendLine("</ol>");
                        inOrderedList = false;
                    }
                    if (inBlockquote)
                    {
                        result.AppendLine("</blockquote>");
                        inBlockquote = false;
                    }
                    if (inTable && tableLines.Count > 0)
                    {
                        result.AppendLine(ProcessTable(tableLines));
                        tableLines.Clear();
                        inTable = false;
                    }
                    
                    // Add paragraph break only if not between list items
                    if (i + 1 < lines.Length && 
                        !IsListItem(lines[i + 1]) && 
                        !string.IsNullOrWhiteSpace(lines[i + 1]))
                    {
                        result.AppendLine("<br/>");
                    }
                    continue;
                }

                // Handle tables
                if (IsTableRow(line))
                {
                    if (!inTable)
                    {
                        inTable = true;
                    }
                    tableLines.Add(line);
                    continue;
                }
                else if (inTable && tableLines.Count > 0)
                {
                    result.AppendLine(ProcessTable(tableLines));
                    tableLines.Clear();
                    inTable = false;
                }

                // Headers
                if (line.StartsWith("# "))
                {
                    result.AppendLine($"<h1>{ProcessInlineMarkdown(line.Substring(2))}</h1>");
                }
                else if (line.StartsWith("## "))
                {
                    result.AppendLine($"<h2>{ProcessInlineMarkdown(line.Substring(3))}</h2>");
                }
                else if (line.StartsWith("### "))
                {
                    result.AppendLine($"<h3>{ProcessInlineMarkdown(line.Substring(4))}</h3>");
                }
                else if (line.StartsWith("#### "))
                {
                    result.AppendLine($"<h4>{ProcessInlineMarkdown(line.Substring(5))}</h4>");
                }
                else if (line.StartsWith("##### "))
                {
                    result.AppendLine($"<h5>{ProcessInlineMarkdown(line.Substring(6))}</h5>");
                }
                else if (line.StartsWith("###### "))
                {
                    result.AppendLine($"<h6>{ProcessInlineMarkdown(line.Substring(7))}</h6>");
                }
                // Alternative headers
                else if (i > 0 && line.StartsWith("===") && !string.IsNullOrWhiteSpace(lines[i - 1]))
                {
                    string headerText = lines[i - 1];
                    // Remove the previous paragraph tag if it exists
                    if (result.ToString().EndsWith($"<p>{ProcessInlineMarkdown(headerText)}</p>\n"))
                    {
                        int startIndex = result.ToString().LastIndexOf($"<p>{ProcessInlineMarkdown(headerText)}</p>\n");
                        result.Remove(startIndex, $"<p>{ProcessInlineMarkdown(headerText)}</p>\n".Length);
                    }
                    result.AppendLine($"<h1>{ProcessInlineMarkdown(headerText)}</h1>");
                }
                else if (i > 0 && line.StartsWith("---") && !line.EndsWith("---") && !string.IsNullOrWhiteSpace(lines[i - 1]))
                {
                    string headerText = lines[i - 1];
                    // Remove the previous paragraph tag if it exists
                    if (result.ToString().EndsWith($"<p>{ProcessInlineMarkdown(headerText)}</p>\n"))
                    {
                        int startIndex = result.ToString().LastIndexOf($"<p>{ProcessInlineMarkdown(headerText)}</p>\n");
                        result.Remove(startIndex, $"<p>{ProcessInlineMarkdown(headerText)}</p>\n".Length);
                    }
                    result.AppendLine($"<h2>{ProcessInlineMarkdown(headerText)}</h2>");
                }
                // Horizontal rule
                else if (IsHorizontalRule(line))
                {
                    result.AppendLine("<hr/>");
                }
                // Lists - unordered
                else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("+ "))
                {
                    if (!inUnorderedList)
                    {
                        result.AppendLine("<ul>");
                        inUnorderedList = true;
                    }
                    
                    // Get indentation level
                    int indent = line.Length - line.TrimStart().Length;
                    string content = line.TrimStart().Substring(2);
                    
                    result.AppendLine($"<li>{ProcessInlineMarkdown(content)}</li>");
                }
                // Lists - ordered
                else if (Regex.IsMatch(line.TrimStart(), @"^\d+\.\s"))
                {
                    if (!inOrderedList)
                    {
                        result.AppendLine("<ol>");
                        inOrderedList = true;
                    }
                    
                    Match match = Regex.Match(line.TrimStart(), @"^(\d+)\.(\s+)(.*)$");
                    if (match.Success)
                    {
                        string content = match.Groups[3].Value;
                        result.AppendLine($"<li>{ProcessInlineMarkdown(content)}</li>");
                    }
                }
                // Blockquotes
                else if (line.TrimStart().StartsWith("> "))
                {
                    if (!inBlockquote)
                    {
                        result.AppendLine("<blockquote>");
                        inBlockquote = true;
                    }
                    string content = line.TrimStart().Substring(2);
                    result.AppendLine($"<p>{ProcessInlineMarkdown(content)}</p>");
                }
                // Task lists
                else if (Regex.IsMatch(line.TrimStart(), @"^-\s+\[([ xX])\]\s"))
                {
                    if (!inUnorderedList)
                    {
                        result.AppendLine("<ul class=\"task-list\">");
                        inUnorderedList = true;
                    }
                    
                    Match match = Regex.Match(line.TrimStart(), @"^-\s+\[([ xX])\]\s(.*)$");
                    if (match.Success)
                    {
                        bool isChecked = match.Groups[1].Value.ToLower() == "x";
                        string content = match.Groups[2].Value;
                        result.AppendLine($"<li class=\"task-list-item\">" +
                            $"<input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled {(isChecked ? "checked" : "")}> " +
                            $"{ProcessInlineMarkdown(content)}</li>");
                    }
                }
                // Default paragraph
                else
                {
                    // Check for multi-line paragraphs
                    StringBuilder paragraphBuilder = new StringBuilder(line);
                    int nextIndex = i + 1;
                    
                    // Collect all adjacent non-empty lines that don't start with a special character
                    while (nextIndex < lines.Length && 
                           !string.IsNullOrWhiteSpace(lines[nextIndex]) && 
                           !IsSpecialLine(lines[nextIndex]))
                    {
                        // Handle line breaks (two spaces at end of line)
                        if (line.EndsWith("  "))
                        {
                            paragraphBuilder.AppendLine("<br />");
                        }
                        else
                        {
                            paragraphBuilder.Append(" ");
                        }
                        
                        paragraphBuilder.Append(lines[nextIndex]);
                        i = nextIndex;
                        nextIndex++;
                    }
                    
                    result.AppendLine($"<p>{ProcessInlineMarkdown(paragraphBuilder.ToString())}</p>");
                }
            }

            // Close any remaining open blocks
            if (inUnorderedList)
            {
                result.AppendLine("</ul>");
            }
            if (inOrderedList)
            {
                result.AppendLine("</ol>");
            }
            if (inBlockquote)
            {
                result.AppendLine("</blockquote>");
            }
            if (inTable && tableLines.Count > 0)
            {
                result.AppendLine(ProcessTable(tableLines));
            }
            if (inCodeBlock)
            {
                result.AppendLine("</code></pre>");
            }

            return result.ToString();
        }

        private static bool IsSpecialLine(string line)
        {
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("#") ||
                   trimmed.StartsWith(">") ||
                   trimmed.StartsWith("-") ||
                   trimmed.StartsWith("*") ||
                   trimmed.StartsWith("+") ||
                   trimmed.StartsWith("```") ||
                   trimmed.StartsWith("---") ||
                   trimmed.StartsWith("===") ||
                   trimmed.StartsWith("|") ||
                   Regex.IsMatch(trimmed, @"^\d+\.\s") ||
                   IsHorizontalRule(trimmed);
        }
        
        private static bool IsListItem(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            
            string trimmed = line.TrimStart();
            return trimmed.StartsWith("- ") || 
                   trimmed.StartsWith("* ") || 
                   trimmed.StartsWith("+ ") ||
                   Regex.IsMatch(trimmed, @"^\d+\.\s");
        }
        
        private static bool IsTableRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            
            // A table row must contain a pipe and have at least one pipe that's not at the beginning or end
            return line.Contains("|") && 
                   ((line.IndexOf("|") != 0 && line.LastIndexOf("|") != line.Length - 1) || 
                    (line.IndexOf("|") == 0 && line.LastIndexOf("|") != line.Length - 1 && line.LastIndexOf("|") != 0) ||
                    (line.LastIndexOf("|") == line.Length - 1 && line.IndexOf("|") != 0));
        }
        
        private static bool IsHorizontalRule(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 3) return false;
            
            return (trimmed.Replace("-", "") == "" && trimmed.Length >= 3) ||
                   (trimmed.Replace("*", "") == "" && trimmed.Length >= 3) ||
                   (trimmed.Replace("_", "") == "" && trimmed.Length >= 3);
        }

        private static string ProcessInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Process escapes first
            text = EscapeRegex.Replace(text, m => m.Groups[1].Value);
            
            // Process images before links (to avoid processing image alt text as links)
            text = ImageRegex.Replace(text, m => {
                string altText = System.Web.HttpUtility.HtmlEncode(m.Groups[1].Value);
                string url = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                return $"<img src=\"{url}\" alt=\"{altText}\" title=\"{altText}\"/>";
            });

            // Process links with target _blank
            text = LinkTargetBlankRegex.Replace(text, m => {
                string linkText = ProcessInlineMarkdown(m.Groups[1].Value); // Process nested markdown in link text
                string url = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                return $"<a href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\">{linkText}</a>";
            });

            // Process standard links
            text = LinkRegex.Replace(text, m => {
                string linkText = ProcessInlineMarkdown(m.Groups[1].Value); // Process nested markdown in link text
                string url = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                return $"<a href=\"{url}\">{linkText}</a>";
            });

            // Process auto-links
            text = AutoLinkRegex.Replace(text, m => {
                string url = System.Web.HttpUtility.HtmlEncode(m.Groups[1].Value);
                return $"<a href=\"{url}\">{url}</a>";
            });

            // Process inline code (must be before other formatting to avoid processing code content)
            text = InlineCodeRegex.Replace(text, m => {
                string code = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                return $"<code>{code}</code>";
            });

            // Process bold/strong (with lookbehind/lookahead to avoid partial matches)
            text = StrongRegex.Replace(text, m => {
                string content = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return $"<strong>{ProcessInlineMarkdown(content)}</strong>";
            });

            // Process italic/emphasis (with lookbehind/lookahead to avoid partial matches)
            text = EmphasisRegex.Replace(text, m => {
                string content = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                return $"<em>{ProcessInlineMarkdown(content)}</em>";
            });

            // Process strikethrough
            text = StrikethroughRegex.Replace(text, m => {
                return $"<del>{ProcessInlineMarkdown(m.Groups[1].Value)}</del>";
            });

            // Replace explicit line breaks
            text = text.Replace("  \n", "<br/>");
            text = text.Replace("  $", "<br/>");

            return text;
        }

        private static string ProcessTable(List<string> tableLines)
        {
            if (tableLines.Count < 2) return string.Empty;

            var result = new StringBuilder();
            result.AppendLine("<table>");
            result.AppendLine("<thead>");

            // Process header row
            var headerCells = ProcessTableRow(tableLines[0]);
            result.AppendLine("<tr>");
            foreach (var cell in headerCells)
            {
                result.AppendLine($"<th>{ProcessInlineMarkdown(cell)}</th>");
            }
            result.AppendLine("</tr>");
            result.AppendLine("</thead>");

            // Process alignment row if present
            bool hasAlignment = tableLines.Count > 1 && tableLines[1].Contains("-");
            string[] alignments = new string[headerCells.Count()];
            
            if (hasAlignment)
            {
                var alignmentCells = ProcessTableRow(tableLines[1]);
                for (int i = 0; i < Math.Min(alignments.Length, alignmentCells.Length); i++)
                {
                    string cell = alignmentCells[i].Trim();
                    if (cell.StartsWith(":") && cell.EndsWith(":"))
                        alignments[i] = " style=\"text-align: center;\"";
                    else if (cell.StartsWith(":"))
                        alignments[i] = " style=\"text-align: left;\"";
                    else if (cell.EndsWith(":"))
                        alignments[i] = " style=\"text-align: right;\"";
                    else
                        alignments[i] = "";
                }
            }

            // Process body rows
            result.AppendLine("<tbody>");
            for (int i = hasAlignment ? 2 : 1; i < tableLines.Count; i++)
            {
                var cells = ProcessTableRow(tableLines[i]);
                result.AppendLine("<tr>");
                for (int j = 0; j < cells.Length; j++)
                {
                    string alignment = j < alignments.Length ? alignments[j] : "";
                    result.AppendLine($"<td{alignment}>{ProcessInlineMarkdown(cells[j])}</td>");
                }
                result.AppendLine("</tr>");
            }
            result.AppendLine("</tbody>");
            result.AppendLine("</table>");
            
            return result.ToString();
        }

        private static string[] ProcessTableRow(string row)
        {
            // Split by pipe character
            string[] cells = row.Split('|');
            
            // Remove empty entries at the beginning and end if they exist
            if (cells.Length > 0 && string.IsNullOrWhiteSpace(cells[0]))
            {
                cells = cells.Skip(1).ToArray();
            }
            
            if (cells.Length > 0 && string.IsNullOrWhiteSpace(cells[cells.Length - 1]))
            {
                cells = cells.Take(cells.Length - 1).ToArray();
            }
            
            // Trim each cell
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = cells[i].Trim();
            }
            
            return cells;
        }
    }
}