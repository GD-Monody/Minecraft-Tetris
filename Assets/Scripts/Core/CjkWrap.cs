using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 中文断行：IMGUI 自带的 wordWrap 只认半角空格，中文「哪里都能断」——
    /// 会把「TNT」拆成 T / NT、把句号顶到下一行开头。这里自己按像素量。
    ///
    /// 规则（按优先级）：
    /// 1. 行首禁则标点（，。、；：？！）宁可让上一行多挤一个字，也不让它开新行；
    /// 2. 英文和数字不拆开，行尾也不挂一个孤零零的英文词；
    /// 3. 最后一行太短（不足半行）时，允许行宽松 4% / 8% / 12% 重断一次，
    ///    能把行数降下来就用 —— 专治「块。」这种孤字。
    ///
    /// 结果有缓存：OnGUI 一帧要跑好几趟，不能每次都逐字量一遍。
    /// </summary>
    public static class CjkWrap
    {
        /// <summary>行首禁则：这些字符排到一行开头很难看。</summary>
        const string NoLineStart = "，。、；：？！）】｝」』”’%°℃";

        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

        static bool IsWordChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        /// <summary>按像素宽度给中文断行，返回用 \n 连起来的结果。</summary>
        /// <param name="allowSlack">
        /// 允不允许为了消除孤字把行宽放大 4%~12%。规则页那种整页排版开着更好看；
        /// 教程的卡片/面板是硬边界，开了就会顶到边框外（用户截图里就是这样），关掉。
        /// </param>
        public static string Wrap(string text, GUIStyle style, float width, bool allowSlack = true)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f || style == null)
                return text;

            string key = text + "|" + Mathf.RoundToInt(width) + "|" + style.fontSize
                + "|" + (allowSlack ? 1 : 0) + "|"
                // 字体可能是 null（皮肤默认字体在运行时就是 null），这里不能直接取 ID
                + (style.font != null ? style.font.GetInstanceID() : 0);
            string cached;
            if (_cache.TryGetValue(key, out cached))
                return cached;

            var sb = new System.Text.StringBuilder(text.Length + 16);
            string[] paras = text.Split('\n');
            for (int pi = 0; pi < paras.Length; pi++)
            {
                if (pi > 0)
                    sb.Append('\n');

                string para = paras[pi];
                string best = WrapPara(para, style, width);

                if (allowSlack && LineCount(best) > 1 && LineWidth(LastLine(best), style) < width * 0.5f)
                {
                    for (int k = 1; k <= 3; k++)
                    {
                        string alt = WrapPara(para, style, width * (1f + 0.04f * k));
                        if (LineCount(alt) < LineCount(best))
                        {
                            best = alt;
                            break;
                        }
                    }
                }
                sb.Append(best);
            }

            string outText = sb.ToString();
            _cache[key] = outText;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-logwrap") >= 0)
                Debug.Log("[wrap] w=" + Mathf.RoundToInt(width) + " fs=" + style.fontSize
                    + " => " + outText.Replace("\n", " / "));
            return outText;
        }

        /// <summary>断好的文字实际多宽：Label 的矩形照它给，免得 IMGUI 再自己折一次行。</summary>
        public static float MaxLineWidth(string wrapped, GUIStyle style)
        {
            if (string.IsNullOrEmpty(wrapped) || style == null)
                return 0f;

            float max = 0f;
            string[] lines = wrapped.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                max = Mathf.Max(max, LineWidth(lines[i], style));
            return max;
        }

        static string WrapPara(string para, GUIStyle style, float width)
        {
            var sb = new System.Text.StringBuilder(para.Length + 8);
            float lineW = 0f;
            int lineStart = 0;
            for (int i = 0; i < para.Length; i++)
            {
                char c = para[i];
                float cw = style.CalcSize(new GUIContent(c.ToString())).x;
                if (lineW + cw <= width || i == lineStart)
                {
                    lineW += cw;
                    continue;
                }

                if (NoLineStart.IndexOf(c) >= 0)
                {
                    // 标点不能出现在行首，所以要塞回本行。但「塞回去」会让这一行超宽，
                    // 外面上限宽度一卡（Label 或标签矩形），IMGUI 又会自己折一次 ——
                    // 结果就是中间多出一个空行（询问卡片上出现过）。
                    // 这里分两种情况：塞得下就直接塞；塞不下就把本行最后一个字也带下去，
                    // 下一行以「字 + 标点」开头，既守禁则也不超宽。
                    if (lineW + cw <= width || i - 1 <= lineStart)
                    {
                        sb.Append(para, lineStart, i - lineStart + 1);
                        sb.Append('\n');
                        lineStart = i + 1;
                        lineW = 0f;
                        continue;
                    }

                    sb.Append(para, lineStart, i - 1 - lineStart);
                    sb.Append('\n');
                    lineStart = i - 1;
                    lineW = 0f;
                    for (int k = lineStart; k <= i; k++)
                        lineW += style.CalcSize(new GUIContent(para[k].ToString())).x;
                    continue;
                }

                int cut = i;
                if (IsWordChar(c))
                {
                    int j = i;
                    while (j > lineStart && IsWordChar(para[j - 1]))
                        j--;
                    if (j > lineStart)
                        cut = j;
                }
                else if (cut > lineStart && IsWordChar(para[cut - 1]))
                {
                    int j = cut;
                    while (j > lineStart && IsWordChar(para[j - 1]))
                        j--;
                    if (j > lineStart)
                        cut = j;
                }

                sb.Append(para, lineStart, cut - lineStart);
                sb.Append('\n');
                lineStart = cut;
                lineW = 0f;
                for (int k = cut; k <= i; k++)
                    lineW += style.CalcSize(new GUIContent(para[k].ToString())).x;
            }
            sb.Append(para, lineStart, para.Length - lineStart);
            return sb.ToString();
        }

        static int LineCount(string s)
        {
            int n = 1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '\n')
                    n++;
            return n;
        }

        static string LastLine(string s)
        {
            int i = s.LastIndexOf('\n');
            return i < 0 ? s : s.Substring(i + 1);
        }

        static float LineWidth(string line, GUIStyle style)
        {
            return line.Length == 0 ? 0f : style.CalcSize(new GUIContent(line)).x;
        }
    }
}
