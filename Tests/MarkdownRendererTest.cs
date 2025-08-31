using System;
using Xunit;
using FluentAssertions;
using NetworkMonitorChat;

namespace NetworkMonitorChat.Tests
{
    public class MarkdownRendererTests
    {
        [Theory]
        [InlineData("# Header 1", "<h1>Header 1</h1>\n")]
        [InlineData("## Header 2", "<h2>Header 2</h2>\n")]
        [InlineData("**bold**", "<p><strong>bold</strong></p>\n")]
        [InlineData("*italic*", "<p><em>italic</em></p>\n")]
        [InlineData("~~strike~~", "<p><del>strike</del></p>\n")]
        [InlineData("`code`", "<p><code>code</code></p>\n")]
        [InlineData("- item1\n- item2", "<ul>\n<li>item1</li>\n<li>item2</li>\n</ul>\n")]
        [InlineData("1. one\n2. two", "<ol>\n<li>one</li>\n<li>two</li>\n</ol>\n")]
        [InlineData("> quote", "<blockquote>\n<p>quote</p>\n</blockquote>\n")]
        [InlineData("---", "<hr/>\n")]
        [InlineData("![alt](url)", "<p><img src=\"url\" alt=\"alt\" title=\"alt\"/></p>\n")]
        [InlineData("[link](http://test.com)", "<p><a href=\"http://test.com\">link</a></p>\n")]
        [InlineData("<https://test.com>", "<p><a href=\"https://test.com\">https://test.com</a></p>\n")]
        public void ToHtml_Should_Render_Basic_Markdown_Correctly(string markdown, string expectedHtml)
        {
            var html = MarkdownRenderer.ToHtml(markdown);
            html.Should().Be(expectedHtml);
        }

        [Fact]
        public void ToHtml_Should_Return_Empty_For_Null_Or_Empty()
        {
            MarkdownRenderer.ToHtml(null).Should().BeEmpty();
            MarkdownRenderer.ToHtml("").Should().BeEmpty();
        }

        [Fact]
        public void ToHtml_Should_Render_Code_Block_With_Language()
        {
            var markdown = "```csharp\nConsole.WriteLine(1);\n```";
            var html = MarkdownRenderer.ToHtml(markdown);
            html.Should().Contain("<pre><code class=\"language-csharp\">");
            html.Should().Contain("Console.WriteLine(1);");
            html.Should().Contain("</code></pre>");
        }

        [Fact]
        public void ToHtml_Should_Render_Code_Block_No_Language()
        {
            var markdown = "```\ncode block\n```";
            var html = MarkdownRenderer.ToHtml(markdown);
            html.Should().Contain("<pre><code class=\"language-\">");
            html.Should().Contain("code block");
            html.Should().Contain("</code></pre>");
        }

        [Fact]
        public void ToHtml_Should_Render_Table_With_Alignment()
        {
            var markdown = "| h1 | h2 |\n|:--:|---:|\n| a  | b  |";
            var html = MarkdownRenderer.ToHtml(markdown);

            html.Should().Contain("<table>");
            html.Should().Contain("<th>h1</th>");
            html.Should().Contain("<th>h2</th>");
            html.Should().Contain("<tbody>");
            html.Should().Contain("<td style=\"text-align: center;\">a</td>");
            html.Should().Contain("<td style=\"text-align: right;\">b</td>");
        }

        [Fact]
        public void ToHtml_Should_Render_Table_Simple()
        {
            var markdown = "| h1 | h2 |\n|----|----|\n| a  | b  |";
            var html = MarkdownRenderer.ToHtml(markdown);
            html.Should().Contain("<table>");
            html.Should().Contain("<th>h1</th>");
            html.Should().Contain("<td>a</td>");
        }

        [Fact]
        public void ToHtml_Should_Not_Italicize_Snake_Case_Or_DoubleUnderscore_InWords()
        {
            MarkdownRenderer.ToHtml("snake_case").Should().Be("<p>snake_case</p>\n");
            MarkdownRenderer.ToHtml("double__underscore").Should().Be("<p>double__underscore</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Italicize_And_Strong_When_Standalone()
        {
            MarkdownRenderer.ToHtml("this _word_ only").Should().Be("<p>this <em>word</em> only</p>\n");
            MarkdownRenderer.ToHtml("this __word__ only").Should().Be("<p>this <strong>word</strong> only</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Not_Format_Underscores_Inside_Inline_Code()
        {
            var md = "`do_not_italicize`";
            MarkdownRenderer.ToHtml(md).Should().Be("<p><code>do_not_italicize</code></p>\n");
        }

        [Fact]
        public void ToHtml_Should_Render_Task_Lists()
        {
            var markdown = "- [x] done\n- [ ] todo";
            var html = MarkdownRenderer.ToHtml(markdown);

            html.Should().Contain("<ul class=\"task-list\">");
            html.Should().Contain("class=\"task-list-item-checkbox\" disabled checked");
            html.Should().Contain("class=\"task-list-item-checkbox\" disabled");
        }

        [Fact]
        public void ToHtml_Should_Handle_Setext_Headers()
        {
            MarkdownRenderer.ToHtml("Title\n===\n").Should().Be("<h1>Title</h1>\n");
            MarkdownRenderer.ToHtml("Subtitle\n---\n").Should().Be("<h2>Subtitle</h2>\n");
        }

        [Fact]
        public void ToHtml_Should_Close_List_Before_Paragraph()
        {
            var md = "- item\nparagraph";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Contain("</ul>\n<p>paragraph</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Close_List_Before_Blockquote()
        {
            var md = "- item\n> quote";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Contain("</ul>\n<blockquote>\n<p>quote</p>\n</blockquote>\n");
        }

        [Fact]
        public void ToHtml_Should_Close_Blockquote_When_Followed_By_Paragraph()
        {
            var md = "> a\nb";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Contain("</blockquote>\n<p>b</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Handle_Hard_Line_Breaks_With_Double_Spaces()
        {
            var md = "line 1  \nline 2";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p>line 1<br />\nline 2</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Combine_Adjacent_Lines_Into_Paragraph()
        {
            var md = "first line\nsecond line";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p>first line second line</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Escape_Raw_Html_And_Ampersands()
        {
            var md = "5 < 6 & 7 > 3";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p>5 &lt; 6 &amp; 7 &gt; 3</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Prevent_Html_Injection()
        {
            var md = "<script>alert(1)</script>";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Render_Link_With_Nested_Strong_Text()
        {
            var md = "[**bold**](http://x)";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p><a href=\"http://x\"><strong>bold</strong></a></p>\n");
        }

        [Fact]
        public void ToHtml_Should_Render_TargetBlank_Links()
        {
            var md = "[ext](https://x){_blank}";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p><a href=\"https://x\" target=\"_blank\" rel=\"noopener noreferrer\">ext</a></p>\n");
        }

        [Fact]
        public void ToHtml_Should_Render_AutoLinks_With_Surrounding_Text()
        {
            var md = "go to <https://a.com> now";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<p>go to <a href=\"https://a.com\">https://a.com</a> now</p>\n");
        }

        [Fact]
        public void ToHtml_Should_Not_Parse_Markdown_Inside_Image_Alt()
        {
            var md = "![*alt*](u)";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Contain("<img src=\"u\" alt=\"*alt*\" title=\"*alt*\"/>");
            html.Should().NotContain("<em>");
        }

        [Fact]
        public void ToHtml_Should_Recognize_Horizontal_Rule_With_Spaces()
        {
            var md = "- - -";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Be("<hr/>\n");
        }

        [Fact]
        public void ToHtml_Should_Close_List_Before_Header()
        {
            var md = "- item\n# Header";
            var html = MarkdownRenderer.ToHtml(md);
            html.Should().Contain("</ul>\n<h1>Header</h1>\n");
        }
    }
}
