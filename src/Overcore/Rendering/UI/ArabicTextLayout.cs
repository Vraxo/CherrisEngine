using Overcore.Components;
using System.Globalization;
using System.Text;

namespace Overcore.Rendering.UI;

/// <summary>A visually ordered grapheme cluster retaining its corresponding logical UTF-16 span.</summary>
public readonly record struct VisualTextCluster(string Text, int LogicalStart, int LogicalLength, bool IsRightToLeft)
{
    public int LogicalEnd => LogicalStart + LogicalLength;
    public int? InternalCaretIndex { get; init; }
}

/// <summary>Arabic-shaped and directionally ordered clusters for one display line.</summary>
public sealed class VisualTextLine(IReadOnlyList<VisualTextCluster> clusters, bool isRightToLeft)
{
    public IReadOnlyList<VisualTextCluster> Clusters { get; } = clusters;
    public bool IsRightToLeft { get; } = isRightToLeft;
    public string Text => String.Concat(Clusters.Select(cluster => cluster.Text));

    /// <summary>Logical caret positions ordered from the visual left edge to the visual right edge.</summary>
    public IReadOnlyList<int> GetVisualCaretStops()
    {
        List<int> stops = [];
        foreach (VisualTextCluster cluster in Clusters)
        {
            int visualLeft = cluster.IsRightToLeft ? cluster.LogicalEnd : cluster.LogicalStart;
            int visualRight = cluster.IsRightToLeft ? cluster.LogicalStart : cluster.LogicalEnd;
            if (stops.Count == 0 || stops[^1] != visualLeft) stops.Add(visualLeft);
            if (cluster.InternalCaretIndex is { } internalCaret
                && internalCaret != visualLeft && internalCaret != visualRight && stops[^1] != internalCaret)
            {
                stops.Add(internalCaret);
            }
            if (stops.Count == 0 || stops[^1] != visualRight) stops.Add(visualRight);
        }
        if (stops.Count == 0) stops.Add(0);
        return stops;
    }

    /// <summary>Gets the physical left-to-right offset of a logical caret using measured visual clusters.</summary>
    public float GetCaretOffset(int logicalIndex, Func<string, float> measureCluster, float characterSpacing)
    {
        Dictionary<int, float> offsets = [];
        float x = 0f;
        bool hasPreviousAdvance = false;
        foreach (VisualTextCluster cluster in Clusters)
        {
            float advance = measureCluster(cluster.Text);
            if (!float.IsFinite(advance) || advance < 0f) advance = 0f;
            if (advance > 0f && hasPreviousAdvance) x += characterSpacing;
            float left = x;
            float right = x + advance;
            if (cluster.IsRightToLeft)
            {
                offsets[cluster.LogicalEnd] = left;
                offsets[cluster.LogicalStart] = right;
            }
            else
            {
                offsets[cluster.LogicalStart] = left;
                offsets[cluster.LogicalEnd] = right;
            }
            if (cluster.InternalCaretIndex is { } internalCaret)
            {
                offsets[internalCaret] = left + (right - left) * 0.5f;
            }
            x = right;
            hasPreviousAdvance |= advance > 0f;
        }
        if (offsets.TryGetValue(logicalIndex, out float offset)) return offset;
        return logicalIndex <= 0 ? (IsRightToLeft ? x : 0f) : (IsRightToLeft ? 0f : x);
    }
}

/// <summary>
/// Shapes Arabic/Persian joining letters with Unicode presentation forms and applies a compact paragraph-level
/// bidi reorder suitable for Arabic/Persian UI text mixed with Latin words and numbers.
/// </summary>
public static class ArabicTextLayout
{
    private enum DirectionClass { Left, Right, Number, Neutral }
    private readonly record struct Forms(int Isolated, int Initial, int Medial, int Final);
    private sealed class Cluster(string text, int start, Rune? baseRune, DirectionClass direction)
    {
        public string Text { get; set; } = text;
        public int Start { get; } = start;
        public int Length { get; set; } = text.Length;
        public Rune? BaseRune { get; } = baseRune;
        public int? InternalCaretIndex { get; set; }
        public DirectionClass Direction { get; set; } = direction;
        public bool RightToLeft => Direction == DirectionClass.Right;
        public int End => Start + Length;
    }

    // Arabic joining-form mappings from the Unicode Arabic Presentation Forms blocks. Persian letters are included.
    // Presentation-form mappings adapted from arabic-reshaper (MIT); see ArabicTextLayout.LICENSE.txt.
    private const string FormData = """
        0621 FE80 0000 0000 0000
        0622 FE81 0000 0000 FE82
        0623 FE83 0000 0000 FE84
        0624 FE85 0000 0000 FE86
        0625 FE87 0000 0000 FE88
        0626 FE89 FE8B FE8C FE8A
        0627 FE8D 0000 0000 FE8E
        0628 FE8F FE91 FE92 FE90
        0629 FE93 0000 0000 FE94
        062A FE95 FE97 FE98 FE96
        062B FE99 FE9B FE9C FE9A
        062C FE9D FE9F FEA0 FE9E
        062D FEA1 FEA3 FEA4 FEA2
        062E FEA5 FEA7 FEA8 FEA6
        062F FEA9 0000 0000 FEAA
        0630 FEAB 0000 0000 FEAC
        0631 FEAD 0000 0000 FEAE
        0632 FEAF 0000 0000 FEB0
        0633 FEB1 FEB3 FEB4 FEB2
        0634 FEB5 FEB7 FEB8 FEB6
        0635 FEB9 FEBB FEBC FEBA
        0636 FEBD FEBF FEC0 FEBE
        0637 FEC1 FEC3 FEC4 FEC2
        0638 FEC5 FEC7 FEC8 FEC6
        0639 FEC9 FECB FECC FECA
        063A FECD FECF FED0 FECE
        0640 0640 0640 0640 0640
        0641 FED1 FED3 FED4 FED2
        0642 FED5 FED7 FED8 FED6
        0643 FED9 FEDB FEDC FEDA
        0644 FEDD FEDF FEE0 FEDE
        0645 FEE1 FEE3 FEE4 FEE2
        0646 FEE5 FEE7 FEE8 FEE6
        0647 FEE9 FEEB FEEC FEEA
        0648 FEED 0000 0000 FEEE
        0649 FEEF FBE8 FBE9 FEF0
        064A FEF1 FEF3 FEF4 FEF2
        0671 FB50 0000 0000 FB51
        0677 FBDD 0000 0000 0000
        0679 FB66 FB68 FB69 FB67
        067A FB5E FB60 FB61 FB5F
        067B FB52 FB54 FB55 FB53
        067E FB56 FB58 FB59 FB57
        067F FB62 FB64 FB65 FB63
        0680 FB5A FB5C FB5D FB5B
        0683 FB76 FB78 FB79 FB77
        0684 FB72 FB74 FB75 FB73
        0686 FB7A FB7C FB7D FB7B
        0687 FB7E FB80 FB81 FB7F
        0688 FB88 0000 0000 FB89
        068C FB84 0000 0000 FB85
        068D FB82 0000 0000 FB83
        068E FB86 0000 0000 FB87
        0691 FB8C 0000 0000 FB8D
        0698 FB8A 0000 0000 FB8B
        06A4 FB6A FB6C FB6D FB6B
        06A6 FB6E FB70 FB71 FB6F
        06A9 FB8E FB90 FB91 FB8F
        06AD FBD3 FBD5 FBD6 FBD4
        06AF FB92 FB94 FB95 FB93
        06B1 FB9A FB9C FB9D FB9B
        06B3 FB96 FB98 FB99 FB97
        06BA FB9E 0000 0000 FB9F
        06BB FBA0 FBA2 FBA3 FBA1
        06BE FBAA FBAC FBAD FBAB
        06C0 FBA4 0000 0000 FBA5
        06C1 FBA6 FBA8 FBA9 FBA7
        06C5 FBE0 0000 0000 FBE1
        06C6 FBD9 0000 0000 FBDA
        06C7 FBD7 0000 0000 FBD8
        06C8 FBDB 0000 0000 FBDC
        06C9 FBE2 0000 0000 FBE3
        06CB FBDE 0000 0000 FBDF
        06CC FBFC FBFE FBFF FBFD
        06D0 FBE4 FBE6 FBE7 FBE5
        06D2 FBAE 0000 0000 FBAF
        06D3 FBB0 0000 0000 FBB1
        """;

    private static readonly Dictionary<int, Forms> s_forms = ParseForms();
    private static readonly Dictionary<int, int> s_mirroredCharacters = new()
    {
        [(int)'('] = ')', [(int)')'] = '(', [(int)'['] = ']', [(int)']'] = '[',
        [(int)'{'] = '}', [(int)'}'] = '{', [(int)'<'] = '>', [(int)'>'] = '<',
        [0x00AB] = 0x00BB, [0x00BB] = 0x00AB,
        [0x2039] = 0x203A, [0x203A] = 0x2039, [0x2045] = 0x2046, [0x2046] = 0x2045
    };

    public static VisualTextLine ShapeLine(string text, TextDirection direction = TextDirection.Auto)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<Cluster> logical = CreateClusters(text);
        bool paragraphRtl = direction switch
        {
            TextDirection.RightToLeft => true,
            TextDirection.LeftToRight => false,
            _ => FindParagraphDirection(logical)
        };
        ResolveNeutralDirections(logical, paragraphRtl);
        ShapeArabicClusters(logical);
        ApplyLamAlefLigatures(logical);

        List<List<Cluster>> runs = [];
        foreach (Cluster cluster in logical)
        {
            if (runs.Count == 0 || runs[^1][0].Direction != cluster.Direction) runs.Add([]);
            runs[^1].Add(cluster);
        }

        if (paragraphRtl) runs.Reverse();
        List<VisualTextCluster> visual = [];
        foreach (List<Cluster> run in runs)
        {
            bool rtl = run[0].RightToLeft;
            if (rtl)
            {
                for (int index = run.Count - 1; index >= 0; index--)
                {
                    Cluster cluster = run[index];
                    visual.Add(new VisualTextCluster(MirrorIfNeeded(cluster.Text, rtl), cluster.Start, cluster.Length, rtl)
                    {
                        InternalCaretIndex = cluster.InternalCaretIndex
                    });
                }
            }
            else
            {
                foreach (Cluster cluster in run)
                {
                    visual.Add(new VisualTextCluster(cluster.Text, cluster.Start, cluster.Length, rtl)
                    {
                        InternalCaretIndex = cluster.InternalCaretIndex
                    });
                }
            }
        }

        return new VisualTextLine(visual, paragraphRtl);
    }

    /// <summary>Moves an input caret one visual cluster left or right while storing its logical string index.</summary>
    public static int MoveCaret(string text, int logicalIndex, int visualDirection, TextDirection direction = TextDirection.Auto)
    {
        IReadOnlyList<int> stops = ShapeLine(text, direction).GetVisualCaretStops();
        int current = 0;
        for (int index = 0; index < stops.Count; index++)
        {
            if (stops[index] == logicalIndex) { current = index; break; }
            if (Math.Abs(stops[index] - logicalIndex) < Math.Abs(stops[current] - logicalIndex)) current = index;
        }
        return stops[Math.Clamp(current + Math.Sign(visualDirection), 0, stops.Count - 1)];
    }

    public static float GetAlignmentOffset(float availableWidth, float textWidth,
        HorizontalTextAlignment alignment, bool rightToLeft) => alignment switch
    {
        HorizontalTextAlignment.Center => (availableWidth - textWidth) * 0.5f,
        HorizontalTextAlignment.Right => availableWidth - textWidth,
        HorizontalTextAlignment.Start => rightToLeft ? availableWidth - textWidth : 0f,
        _ => 0f
    };

    private static List<Cluster> CreateClusters(string text)
    {
        List<Cluster> clusters = [];
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            Rune? baseRune = FindBaseRune(element);
            DirectionClass direction = baseRune is { } rune ? Classify(rune) : DirectionClass.Neutral;
            clusters.Add(new Cluster(element, elements.ElementIndex, baseRune, direction));
        }
        return clusters;
    }

    private static Rune? FindBaseRune(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            {
                continue;
            }
            return rune;
        }
        return null;
    }

    private static DirectionClass Classify(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        if (category == UnicodeCategory.DecimalDigitNumber) return DirectionClass.Number;
        if (!Rune.IsLetter(rune)) return DirectionClass.Neutral;
        int value = rune.Value;
        bool arabicOrHebrew = value is >= 0x0590 and <= 0x08FF
            or >= 0xFB1D and <= 0xFDFF
            or >= 0xFE70 and <= 0xFEFF
            or >= 0x10800 and <= 0x10FFF;
        return arabicOrHebrew ? DirectionClass.Right : DirectionClass.Left;
    }

    private static bool FindParagraphDirection(IEnumerable<Cluster> clusters)
    {
        foreach (Cluster cluster in clusters)
        {
            if (cluster.Direction == DirectionClass.Right) return true;
            if (cluster.Direction == DirectionClass.Left) return false;
        }
        return false;
    }

    private static void ResolveNeutralDirections(List<Cluster> clusters, bool paragraphRtl)
    {
        for (int index = 0; index < clusters.Count;)
        {
            if (clusters[index].Direction != DirectionClass.Neutral) { index++; continue; }
            int start = index;
            while (index < clusters.Count && clusters[index].Direction == DirectionClass.Neutral) index++;
            DirectionClass before = paragraphRtl ? DirectionClass.Right : DirectionClass.Left;
            DirectionClass after = before;
            for (int scan = start - 1; scan >= 0; scan--)
            {
                if (TryGetResolvedDirection(clusters[scan], out before)) break;
            }
            for (int scan = index; scan < clusters.Count; scan++)
            {
                if (TryGetResolvedDirection(clusters[scan], out after)) break;
            }
            bool resolvedRtl = before == after
                ? before == DirectionClass.Right
                : paragraphRtl;
            for (int scan = start; scan < index; scan++)
            {
                clusters[scan].Direction = resolvedRtl ? DirectionClass.Right : DirectionClass.Left;
            }
        }
    }

    private static bool TryGetResolvedDirection(Cluster cluster, out DirectionClass direction)
    {
        direction = cluster.Direction;
        return direction != DirectionClass.Neutral;
    }

    private static void ShapeArabicClusters(List<Cluster> clusters)
    {
        for (int index = 0; index < clusters.Count; index++)
        {
            Cluster cluster = clusters[index];
            if (cluster.BaseRune is not { } rune || !s_forms.TryGetValue(rune.Value, out Forms form)) continue;
            Forms? previous = FindJoiningNeighbor(clusters, index, -1);
            Forms? next = FindJoiningNeighbor(clusters, index, 1);
            bool joinsPrevious = previous is { } previousForm && form.Final != 0
                && (previousForm.Initial != 0 || previousForm.Medial != 0);
            bool joinsNext = next is { } nextForm && form.Initial != 0
                && (nextForm.Final != 0 || nextForm.Medial != 0);
            int presentation = joinsPrevious && joinsNext ? form.Medial
                : joinsPrevious ? form.Final
                : joinsNext ? form.Initial
                : form.Isolated;
            if (presentation != 0) cluster.Text = ReplaceBaseRune(cluster.Text, rune, new Rune(presentation));
        }

        foreach (Cluster cluster in clusters)
        {
            cluster.Text = RemoveJoiningControls(cluster.Text);
        }
    }

    private static void ApplyLamAlefLigatures(List<Cluster> clusters)
    {
        for (int index = 0; index + 1 < clusters.Count; index++)
        {
            Cluster lam = clusters[index];
            if (lam.BaseRune is not { Value: 0x0644 } || clusters[index + 1].BaseRune is not { } alef) continue;
            int isolated = alef.Value switch
            {
                0x0622 => 0xFEF5,
                0x0623 => 0xFEF7,
                0x0625 => 0xFEF9,
                0x0627 => 0xFEFB,
                _ => 0
            };
            if (isolated == 0) continue;

            Forms? previous = FindJoiningNeighbor(clusters, index, -1);
            bool joinsPrevious = previous is { } previousForm && lam.BaseRune is { } lamRune
                && s_forms.TryGetValue(lamRune.Value, out Forms lamForms)
                && lamForms.Final != 0 && (previousForm.Initial != 0 || previousForm.Medial != 0);
            int ligature = joinsPrevious ? isolated + 1 : isolated;
            Cluster alefCluster = clusters[index + 1];
            lam.InternalCaretIndex = lam.End;
            lam.Length += alefCluster.Length;
            lam.Text = new Rune(ligature).ToString() + GetMarksAfterBase(lam.Text) + GetMarksAfterBase(alefCluster.Text);
            clusters.RemoveAt(index + 1);
        }
    }

    private static string GetMarksAfterBase(string text)
    {
        StringBuilder result = new(text.Length);
        bool removedBase = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!removedBase && Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format))
            {
                removedBase = true;
                continue;
            }
            if (rune.Value is not 0x200C and not 0x200D) result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private static Forms? FindJoiningNeighbor(List<Cluster> clusters, int index, int direction)
    {
        for (int neighborIndex = index + direction;
            neighborIndex >= 0 && neighborIndex < clusters.Count;
            neighborIndex += direction)
        {
            Cluster neighbor = clusters[neighborIndex];
            if (neighbor.Text.Contains('\u200C')) return null;
            if (neighbor.Text.Contains('\u200D') && neighbor.BaseRune is null) continue;
            if (neighbor.BaseRune is { } rune && s_forms.TryGetValue(rune.Value, out Forms forms)) return forms;
            return null;
        }
        return null;
    }

    private static string RemoveJoiningControls(string text)
    {
        bool hasFormat = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) { hasFormat = true; break; }
        }
        if (!hasFormat) return text;
        StringBuilder result = new(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private static string ReplaceBaseRune(string text, Rune original, Rune replacement)
    {
        StringBuilder result = new(text.Length);
        bool replaced = false;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!replaced && rune == original)
            {
                result.Append(replacement.ToString());
                replaced = true;
            }
            else if (rune.Value is not 0x200C and not 0x200D)
            {
                result.Append(rune.ToString());
            }
        }
        return result.ToString();
    }

    private static string MirrorIfNeeded(string text, bool rtl)
    {
        if (!rtl) return text;
        StringBuilder result = new(text.Length);
        foreach (Rune rune in text.EnumerateRunes())
        {
            result.Append(s_mirroredCharacters.TryGetValue(rune.Value, out int mirrored)
                ? new Rune(mirrored).ToString()
                : rune.ToString());
        }
        return result.ToString();
    }

    private static Dictionary<int, Forms> ParseForms()
    {
        Dictionary<int, Forms> forms = [];
        foreach (string line in FormData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length != 5) continue;
            int[] values = columns.Select(value => Convert.ToInt32(value, 16)).ToArray();
            forms[values[0]] = new Forms(values[1], values[2], values[3], values[4]);
        }
        return forms;
    }
}
