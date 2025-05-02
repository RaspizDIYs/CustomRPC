using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Linq; // Добавлено для LastInline

namespace CustomMediaRPC.Utils // Убедись, что это твое пространство имен
{
    public static class MarkdownUtils
    {
        // Метод для форматирования TextBlock из Markdown строки
        public static void FormatTextBlock(TextBlock textBlock, string markdown)
        {
            if (textBlock == null) return;

            textBlock.Inlines.Clear();
            if (string.IsNullOrEmpty(markdown)) return;

            // Используем Markdig для парсинга
            var pipeline = new MarkdownPipelineBuilder().Build();
            var document = Markdown.Parse(markdown, pipeline);

            foreach (var block in document)
            {
                // Пока обрабатываем только параграфы
                if (block is ParagraphBlock paragraph)
                {
                    AppendInlinesRecursive(textBlock.Inlines, paragraph.Inline, textBlock.FontWeight, textBlock.FontStyle);
                    // Добавим перенос строки после параграфа для лучшего разделения, если это не последний блок
                    if (block != document.Last())
                    {
                         textBlock.Inlines.Add(new LineBreak());
                    }
                }
                // Добавим обработку списков
                else if (block is ListBlock listBlock)
                {
                    // Пройдемся по элементам списка
                    foreach (var item in listBlock)
                    {
                        if (item is ListItemBlock listItem)
                        {
                            // Добавим маркер списка (можно настроить вид)
                            string bullet = listBlock.IsOrdered ? $"{listItem.Order}. " : "• "; // Используем Order для нумерованных списков
                            textBlock.Inlines.Add(new Run(bullet) { FontWeight = textBlock.FontWeight, FontStyle = textBlock.FontStyle });

                            // Элемент списка обычно содержит параграф (или другие блоки)
                            foreach(var subBlock in listItem)
                            {
                                // Чаще всего это ParagraphBlock, но могут быть и другие контейнеры
                                if (subBlock is ParagraphBlock itemParagraph && itemParagraph.Inline != null)
                                {
                                    AppendInlinesRecursive(textBlock.Inlines, itemParagraph.Inline, textBlock.FontWeight, textBlock.FontStyle);
                                }
                                // Сюда можно добавить обработку других вложенных блоков, если нужно
                                else if (subBlock is LeafBlock leafBlock) // На случай если там просто текст без параграфа
                                {
                                     if(leafBlock.Inline != null) AppendInlinesRecursive(textBlock.Inlines, leafBlock.Inline, textBlock.FontWeight, textBlock.FontStyle);
                                }
                                else if (subBlock is ContainerBlock containerBlock) // Обработка других контейнеров рекурсивно? (пока не нужно)
                                {
                                     // TODO: Handle nested containers if needed
                                }
                            }
                            // Добавим перенос строки после каждого элемента списка
                            textBlock.Inlines.Add(new LineBreak());
                        }
                    }
                     // Уберем последний LineBreak, добавленный циклом, чтобы не было лишнего отступа после списка
                     if (textBlock.Inlines.LastInline is LineBreak lastListBreak)
                     {
                         textBlock.Inlines.Remove(lastListBreak);
                     }
                     // Добавим перенос строки после всего списка, если это не последний блок
                     if (block != document.Last())
                     {
                         textBlock.Inlines.Add(new LineBreak());
                     }
                }
                // Сюда можно добавить обработку других блоков (заголовки, цитаты и т.д.), если нужно
            }
             // Больше не нужно убирать LineBreak здесь, так как мы добавляем их только между блоками
            // if (textBlock.Inlines.LastInline is LineBreak lastLineBreak)
            // {
            //     textBlock.Inlines.Remove(lastLineBreak);
            // }
        }

        // Рекурсивный метод для обхода Inline элементов и применения стилей
        private static void AppendInlinesRecursive(InlineCollection parentInlines, ContainerInline? container, FontWeight currentWeight, FontStyle currentStyle)
        {
            if (container == null) return;

            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        // Обычный текст - создаем Run с текущими стилями
                        parentInlines.Add(new Run(literal.Content.ToString()) { FontWeight = currentWeight, FontStyle = currentStyle });
                        break;

                    case EmphasisInline emphasis:
                        // Элемент выделения (*italic*, **bold**, ***bold italic***, __bold__, _italic_)
                        // Определяем новый стиль на основе типа выделения
                        FontWeight newWeight = currentWeight;
                        FontStyle newStyle = currentStyle;

                        // Заменяем IsDoubleEmphasis на DelimiterCount
                        // if (emphasis.IsDoubleEmphasis) // ** или __ -> Bold
                        if (emphasis.DelimiterCount == 2) // ** или __ -> Bold
                        {
                            newWeight = FontWeights.Bold;
                        }
                        // else // * или _ -> Italic
                        else if (emphasis.DelimiterCount == 1) // * или _ -> Italic
                        {
                            newStyle = FontStyles.Italic;
                        }
                        // Если DelimiterCount > 2 (например, ***), Markdig обычно обрабатывает это как вложенные Emphasis,
                        // поэтому рекурсивный вызов должен справиться.

                        // Рекурсивно обрабатываем вложенные элементы с новым стилем
                        AppendInlinesRecursive(parentInlines, emphasis, newWeight, newStyle);
                        break;

                    case LineBreakInline softBreak: // Это обычно мягкий перенос строки внутри параграфа
                         // В WPF TextBlock мягкие переносы обычно не требуют <LineBreak/>, пробела достаточно,
                         // если только это не двойной пробел в конце строки по спецификации Markdown (IsHard)
                         if (softBreak.IsHard)
                         {
                            parentInlines.Add(new LineBreak());
                         }
                         else
                         {
                             // Мягкий перенос (<br> не в конце строки) часто игнорируется или преобразуется в пробел
                             // Проверяем, не является ли последний добавленный элемент уже пробелом
                            if (!(parentInlines.LastInline is Run run && run.Text.EndsWith(" ")))
                            {
                                parentInlines.Add(new Run(" "));
                            }
                         }
                        break;

                    default:
                        // Неизвестный элемент - пытаемся добавить как текст
                        if (inline is LeafInline leaf)
                        {
                            var content = (inline as LiteralInline)?.Content.ToString() ?? inline.ToString() ?? string.Empty;
                             parentInlines.Add(new Run(content) { FontWeight = currentWeight, FontStyle = currentStyle });
                        }
                        // Если это контейнер, попробуем обработать его содержимое рекурсивно
                        else if (inline is ContainerInline innerContainer)
                        {
                            AppendInlinesRecursive(parentInlines, innerContainer, currentWeight, currentStyle);
                        }
                        break;
                }
            }
        }

        // --- КОД ДЛЯ ATTACHED PROPERTY ---

        public static readonly DependencyProperty MarkdownTextProperty =
            DependencyProperty.RegisterAttached(
                "MarkdownText", // Имя свойства
                typeof(string), // Тип свойства
                typeof(MarkdownUtils), // Класс-владелец
                new PropertyMetadata(string.Empty, OnMarkdownTextChanged)); // Callback при изменении

        // Getter для свойства
        public static string GetMarkdownText(DependencyObject obj)
        {
            return (string)obj.GetValue(MarkdownTextProperty);
        }

        // Setter для свойства
        public static void SetMarkdownText(DependencyObject obj, string value)
        {
            obj.SetValue(MarkdownTextProperty, value);
        }

        // Callback, вызываемый при изменении значения MarkdownText
        private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Убедимся, что это TextBlock
            if (d is TextBlock textBlock)
            {
                // Получаем новое значение Markdown
                string markdown = e.NewValue as string ?? string.Empty;
                // Вызываем наш метод форматирования
                FormatTextBlock(textBlock, markdown);
            }
        }
    }
} 