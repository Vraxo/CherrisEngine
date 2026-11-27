using System.Globalization;

namespace Overcore.Rendering.UI;

/// <summary>Breaks text into width-constrained lines while retaining explicit newlines and long-word readability.</summary>
public static class TextLayout
{
    public static IReadOnlyList<string> Wrap(string content, float maxWidth, Func<string, float> measure)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(measure);
        if (!float.IsFinite(maxWidth) || maxWidth <= 0f)
        {
            return content.Replace("\r", String.Empty).Split('\n');
        }
        float SafeMeasure(string text)
        {
            float width = measure(text);
            return float.IsFinite(width) ? MathF.Max(0f, width) : float.PositiveInfinity;
        }

        List<string> result = [];
        foreach (string paragraph in content.Replace("\r", String.Empty).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add(String.Empty);
                continue;
            }

            string line = String.Empty;
            foreach (string word in paragraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : $"{line} {word}";
                if (SafeMeasure(candidate) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    result.Add(line);
                    line = String.Empty;
                }

                foreach (string chunk in SplitLongWord(word, maxWidth, SafeMeasure))
                {
                    if (line.Length > 0)
                    {
                        result.Add(line);
                    }
                    line = chunk;
                }
            }

            result.Add(line);
        }

        return result;
    }

    private static IEnumerable<string> SplitLongWord(string word, float maxWidth, Func<string, float> measure)
    {
        string chunk = String.Empty;
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(word);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            string candidate = chunk + element;
            if (chunk.Length > 0 && measure(candidate) > maxWidth)
            {
                yield return chunk;
                chunk = element;
            }
            else
            {
                chunk = candidate;
            }
        }

        if (chunk.Length > 0)
        {
            yield return chunk;
        }
    }
}
