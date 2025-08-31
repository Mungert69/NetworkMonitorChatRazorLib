using Microsoft.AspNetCore.Components;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace NetworkMonitorChat
{
    public partial class MarkdownRenderer
    {
        [Parameter]
        public string Content { get; set; }

        // --- Inline regexes (compiled) ---
        private static readonly Regex ImageRegex = new(@"\!\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);
        private static readonly Regex LinkTargetBlankRegex = new(@"\[(.*?)\]\((.*?)\)\{_blank\}", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new(@"\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);

        // Support both raw <url> and &lt;url&gt; (after neutralization)
        private static readonly Regex AutoLinkRegex =
            new(@"(?:<(https?://[^\s>]+)>|&lt;(https?://[^\s>]+)&gt;)", RegexOptions.Compiled);

        // Emphasis/Strong guarded to avoid underscores inside words
        private static readonly Regex StrongRegex =
            new(@"(?<!\w)\*\*(.+?)\*\*(?!\w)|(?<!\w)__(.+?)__(?!\w)", RegexOptions.Compiled);

        private static readonly Regex EmphasisRegex =
            new(@"(?<!\w)\*(?!\s)(.+?)(?<!\s)\*(?!\w)|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)", RegexOptions.Compiled);

        private static readonly Regex StrikethroughRegex = new(@"~~(.*?)~~", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new(@"(`+)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex EscapeRegex = new(@"\\([\\`*_{}\[\]()#+\-.!])", RegexOptions.Compiled);

        public static string ToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            markdown = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

            var result = new StringBuilder();
            var lines = markdown.Split('\n');
            bool inCodeBlock = false;
            bool inUnorderedList = false;
            bool inOrderedList = false;
            bool inBlockquote = false;
            bool inTable = false;
            var tableLines = new List<string>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Fenced code
                if (line.StartsWith("```"))
                {
                    if (inUnorderedList) { result.AppendLine("</ul>"); inUnorderedList = false; }
                    if (inOrderedList) { result.AppendLine("</ol>"); inOrderedList = false; }
                    if (inBlockquote) { result.AppendLine("</blockquote>"); inBlockquote = false; }

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
                    result.AppendLine(System.Web.HttpUtility.HtmlEncode(line));
                    continue;
                }

                // Blank line: close blocks, flush tables, optional <br/>
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (inUnorderedList) { result.AppendLine("</ul>"); inUnorderedList = false; }
                    if (inOrderedList) { result.AppendLine("</ol>"); inOrderedList = false; }
                    if (inBlockquote) { result.AppendLine("</blockquote>"); inBlockquote = false; }
                    if (inTable && tableLines.Count > 0)
                    {
                        result.AppendLine(ProcessTable(tableLines));
                        tableLines.Clear();
                        inTable = false;
                    }

                    if (i + 1 < lines.Length &&
                        !IsListItem(lines[i + 1]) &&
                        !string.IsNullOrWhiteSpace(lines[i + 1]))
                    {
                        result.AppendLine("<br/>");
                    }
                    continue;
                }

                // Table aggregation
                if (IsTableRow(line))
                {
                    inTable = true;
                    tableLines.Add(line);
                    continue;
                }
                else if (inTable && tableLines.Count > 0)
                {
                    result.AppendLine(ProcessTable(tableLines));
                    tableLines.Clear();
                    inTable = false;
                }

                // Determine line kinds
                string trimmed = line.TrimStart();
                bool isTaskLine = Regex.IsMatch(trimmed, @"^-\s+\[([ xX])\]\s");
                bool isULine = trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("+ ");
                bool isOLine = Regex.IsMatch(trimmed, @"^\d+\.\s");
                bool isBqLine = trimmed.StartsWith("> ");
                bool isHR = IsHorizontalRule(line);

                // Setext header (current line is underline)
                string underline = line.Trim();
                bool isH1Underline = underline.Length >= 3 && underline.All(c => c == '=');
                bool isH2Underline = underline.Length >= 3 && underline.All(c => c == '-');
                if ((isH1Underline || isH2Underline) && i > 0 && !string.IsNullOrWhiteSpace(lines[i - 1]))
                {
                    string headerText = lines[i - 1];
                    string prevPara = $"<p>{ProcessInlineMarkdown(headerText)}</p>\n";
                    string current = result.ToString();
                    int idx = current.LastIndexOf(prevPara, System.StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        result.Clear();
                        result.Append(current[..idx]);
                    }

                    result.AppendLine(isH1Underline
                        ? $"<h1>{ProcessInlineMarkdown(headerText)}</h1>"
                        : $"<h2>{ProcessInlineMarkdown(headerText)}</h2>");
                    continue;
                }

                // Close lists if this isn't a list item
                if (!(isTaskLine || isULine || isOLine))
                {
                    if (inUnorderedList) { result.AppendLine("</ul>"); inUnorderedList = false; }
                    if (inOrderedList) { result.AppendLine("</ol>"); inOrderedList = false; }
                }

                // Close blockquote if the current line is not a quote
                if (!isBqLine && inBlockquote)
                {
                    result.AppendLine("</blockquote>");
                    inBlockquote = false;
                }

                // ATX headers
                if (line.StartsWith("# ")) { result.AppendLine($"<h1>{ProcessInlineMarkdown(line[2..])}</h1>"); continue; }
                if (line.StartsWith("## ")) { result.AppendLine($"<h2>{ProcessInlineMarkdown(line[3..])}</h2>"); continue; }
                if (line.StartsWith("### ")) { result.AppendLine($"<h3>{ProcessInlineMarkdown(line[4..])}</h3>"); continue; }
                if (line.StartsWith("#### ")) { result.AppendLine($"<h4>{ProcessInlineMarkdown(line[5..])}</h4>"); continue; }
                if (line.StartsWith("##### ")) { result.AppendLine($"<h5>{ProcessInlineMarkdown(line[6..])}</h5>"); continue; }
                if (line.StartsWith("###### ")) { result.AppendLine($"<h6>{ProcessInlineMarkdown(line[7..])}</h6>"); continue; }

                // Horizontal rule
                if (isHR)
                {
                    result.AppendLine("<hr/>");
                    continue;
                }

                // Blockquote
                if (isBqLine)
                {
                    if (inUnorderedList) { result.AppendLine("</ul>"); inUnorderedList = false; }
                    if (inOrderedList) { result.AppendLine("</ol>"); inOrderedList = false; }
                    if (!inBlockquote)
                    {
                        result.AppendLine("<blockquote>");
                        inBlockquote = true;
                    }
                    string content = trimmed.Substring(2);
                    result.AppendLine($"<p>{ProcessInlineMarkdown(content)}</p>");
                    continue;
                }

                // Task list (before generic UL)
                if (isTaskLine)
                {
                    if (!inUnorderedList)
                    {
                        result.AppendLine("<ul class=\"task-list\">");
                        inUnorderedList = true;
                    }
                    Match match = Regex.Match(trimmed, @"^-\s+\[([ xX])\]\s(.*)$");
                    if (match.Success)
                    {
                        bool isChecked = match.Groups[1].Value.ToLower() == "x";
                        string content = match.Groups[2].Value;
                        result.AppendLine(
                            $"<li class=\"task-list-item\">" +
                            $"<input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled {(isChecked ? "checked" : "")}> " +
                            $"{ProcessInlineMarkdown(content)}</li>");
                    }
                    continue;
                }

                // UL
                if (isULine)
                {
                    if (!inUnorderedList)
                    {
                        result.AppendLine("<ul>");
                        inUnorderedList = true;
                    }
                    string content = trimmed.Substring(2);
                    result.AppendLine($"<li>{ProcessInlineMarkdown(content)}</li>");
                    continue;
                }

                // OL
                if (isOLine)
                {
                    if (!inOrderedList)
                    {
                        result.AppendLine("<ol>");
                        inOrderedList = true;
                    }
                    Match match = Regex.Match(trimmed, @"^(\d+)\.(\s+)(.*)$");
                    if (match.Success)
                    {
                        string content = match.Groups[3].Value;
                        result.AppendLine($"<li>{ProcessInlineMarkdown(content)}</li>");
                    }
                    continue;
                }

                // Default paragraph (stitch adjacent non-special lines)
                var paragraphBuilder = new StringBuilder(line);
                int nextIndex = i + 1;

                while (nextIndex < lines.Length &&
                       !string.IsNullOrWhiteSpace(lines[nextIndex]) &&
                       !IsSpecialLine(lines[nextIndex]))
                {
                    if (lines[nextIndex - 1].EndsWith("  "))
                    {
                        // remove trailing spaces before adding the hard break
                        while (paragraphBuilder.Length > 0 && paragraphBuilder[^1] == ' ')
                            paragraphBuilder.Length--;

                        // insert a placeholder so ProcessInlineMarkdown doesn't escape it
                        paragraphBuilder.AppendLine("\u0001BR\u0001");
                    }
                    else
                    {
                        paragraphBuilder.Append(" ");
                    }

                    paragraphBuilder.Append(lines[nextIndex]);
                    i = nextIndex;
                    nextIndex++;
                }

                // process inline markdown first, then restore <br />
                string processedPara = ProcessInlineMarkdown(paragraphBuilder.ToString())
                    .Replace("\u0001BR\u0001", "<br />");

                result.AppendLine($"<p>{processedPara}</p>");

            }

            // Close remaining blocks
            if (inUnorderedList) result.AppendLine("</ul>");
            if (inOrderedList) result.AppendLine("</ol>");
            if (inBlockquote) result.AppendLine("</blockquote>");
            if (inTable && tableLines.Count > 0) result.AppendLine(ProcessTable(tableLines));
            if (inCodeBlock) result.AppendLine("</code></pre>");

            return result.ToString();
        }

        private static bool IsSpecialLine(string line)
        {
            string t = line?.TrimStart() ?? "";
            return t.StartsWith("#") ||
                   t.StartsWith(">") ||
                   t.StartsWith("- ") ||
                   t.StartsWith("* ") ||
                   t.StartsWith("+ ") ||
                   Regex.IsMatch(t, @"^-\s+\[([ xX])\]\s") ||
                   t.StartsWith("```") ||
                   t.StartsWith("---") ||
                   t.StartsWith("===") ||
                   t.StartsWith("|") ||
                   Regex.IsMatch(t, @"^\d+\.\s") ||
                   IsHorizontalRule(t);
        }

        private static bool IsListItem(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string t = line.TrimStart();
            return t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ ") ||
                   Regex.IsMatch(t, @"^\d+\.\s") ||
                   Regex.IsMatch(t, @"^-\s+\[([ xX])\]\s");
        }

        private static bool IsTableRow(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string trimmed = line.Trim();
            if (!trimmed.Contains("|")) return false;
            int pipeCount = trimmed.Count(c => c == '|');
            return pipeCount >= 2;
        }

        private static bool IsHorizontalRule(string line)
        {
            string t = (line ?? "").Replace(" ", "").Trim();
            if (t.Length < 3) return false;
            return t.All(c => c == '-') || t.All(c => c == '*') || t.All(c => c == '_');
        }

        // --- Inline processing ---
        private static string ProcessInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Backslash escapes first (raw)
            text = EscapeRegex.Replace(text, m => m.Groups[1].Value);

            // Neutralize raw HTML early
            text = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

            // Protect images with placeholders so alt="" doesn't get formatted
            var imgTags = new List<string>();
            text = ImageRegex.Replace(text, m =>
            {
                string altRaw = m.Groups[1].Value;
                string urlRaw = m.Groups[2].Value;

                string altAttr = System.Web.HttpUtility.HtmlAttributeEncode(altRaw);
                string urlAttr = System.Web.HttpUtility.HtmlAttributeEncode(urlRaw);

                string tag = $"<img src=\"{urlAttr}\" alt=\"{altAttr}\" title=\"{altAttr}\"/>";
                imgTags.Add(tag);
                return $"\u0001IMG{imgTags.Count - 1}\u0001";
            });

            // Links
            text = LinkTargetBlankRegex.Replace(text, m =>
            {
                string linkText = ProcessInlineMarkdown(m.Groups[1].Value);
                string urlAttr = System.Web.HttpUtility.HtmlAttributeEncode(m.Groups[2].Value);
                return $"<a href=\"{urlAttr}\" target=\"_blank\" rel=\"noopener noreferrer\">{linkText}</a>";
            });

            text = LinkRegex.Replace(text, m =>
            {
                string linkText = ProcessInlineMarkdown(m.Groups[1].Value);
                string urlAttr = System.Web.HttpUtility.HtmlAttributeEncode(m.Groups[2].Value);
                return $"<a href=\"{urlAttr}\">{linkText}</a>";
            });

            // Autolinks (<url> and &lt;url&gt;)
            text = AutoLinkRegex.Replace(text, m =>
            {
                string urlRaw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                string href = System.Web.HttpUtility.HtmlAttributeEncode(urlRaw);
                string visible = System.Web.HttpUtility.HtmlEncode(urlRaw);
                return $"<a href=\"{href}\">{visible}</a>";
            });

            // Inline code
            text = InlineCodeRegex.Replace(text, m =>
            {
                string code = System.Web.HttpUtility.HtmlEncode(m.Groups[2].Value);
                return $"<code>{code}</code>";
            });

            // Apply emphasis/strong/strike outside tags only (don’t touch attributes)
            text = ApplyFormattingOutsideTags(text, s =>
            {
                s = StrongRegex.Replace(s, m =>
                {
                    string content = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    return $"<strong>{content}</strong>";
                });

                s = EmphasisRegex.Replace(s, m =>
                {
                    string content = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    return $"<em>{content}</em>";
                });

                s = StrikethroughRegex.Replace(s, m => $"<del>{m.Groups[1].Value}</del>");
                return s;
            });

            // Restore image placeholders
            for (int i = 0; i < imgTags.Count; i++)
                text = text.Replace($"\u0001IMG{i}\u0001", imgTags[i]);

            return text;
        }

        private static string ApplyFormattingOutsideTags(string input, System.Func<string, string> format)
        {
            var parts = Regex.Split(input, "(<[^>]+>)");
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (parts[i][0] == '<') continue; // skip tags
                parts[i] = format(parts[i]);
            }
            return string.Concat(parts);
        }

        private static string ProcessTable(List<string> tableLines)
        {
            if (tableLines.Count < 2) return string.Empty;

            var result = new StringBuilder();
            result.AppendLine("<table>");
            result.AppendLine("<thead>");

            var headerCells = ProcessTableRow(tableLines[0]);
            result.AppendLine("<tr>");
            foreach (var cell in headerCells)
                result.AppendLine($"<th>{ProcessInlineMarkdown(cell)}</th>");
            result.AppendLine("</tr>");
            result.AppendLine("</thead>");

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
            string[] cells = row.Split('|');
            if (cells.Length > 0 && string.IsNullOrWhiteSpace(cells[0])) cells = cells.Skip(1).ToArray();
            if (cells.Length > 0 && string.IsNullOrWhiteSpace(cells[^1])) cells = cells.Take(cells.Length - 1).ToArray();
            for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim();
            return cells;
        }
    }
}
