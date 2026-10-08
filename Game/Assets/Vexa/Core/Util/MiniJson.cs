using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vexa.Core
{
    /// <summary>
    /// Minimal JSON reader/writer (the core has no dependencies). Objects become
    /// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var v = Value(json, ref i);
            Ws(json, ref i);
            if (i != json.Length) throw new FormatException($"JSON: unexpected '{json[i]}' at {i}");
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                // allow // comments in hand-written config files
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                for (;;)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException($"JSON: ':' expected at {i}");
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException($"JSON: ',' or '}}' expected at {i}");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                for (;;)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException($"JSON: ',' or ']' expected at {i}");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException($"JSON: unexpected '{c}' at {i}");
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException($"JSON: string expected at {i}");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        // ---------------- writing ----------------

        public static string Write(object v, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, v, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, bool pretty, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: Quote(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case System.Collections.IDictionary dict:
                    {
                        sb.Append('{');
                        bool first = true;
                        foreach (System.Collections.DictionaryEntry kv in dict)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Indent(sb, pretty, depth + 1);
                            Quote(sb, kv.Key.ToString());
                            sb.Append(pretty ? ": " : ":");
                            Write(sb, kv.Value, pretty, depth + 1);
                        }
                        if (!first) Indent(sb, pretty, depth);
                        sb.Append('}');
                        return;
                    }
                case System.Collections.IEnumerable list:
                    {
                        sb.Append('[');
                        bool first = true;
                        foreach (var item in list)
                        {
                            if (!first) sb.Append(',');
                            first = false;
                            Indent(sb, pretty, depth + 1);
                            Write(sb, item, pretty, depth + 1);
                        }
                        if (!first) Indent(sb, pretty, depth);
                        sb.Append(']');
                        return;
                    }
                default: Quote(sb, v.ToString()); return;
            }
        }

        static void Indent(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- typed access helpers ----------------

        public static string Str(this Dictionary<string, object> d, string key, string def = null) => d.TryGetValue(key, out var v) && v != null ? v.ToString() : def;
        public static int Int(this Dictionary<string, object> d, string key, int def) => d.TryGetValue(key, out var v) && v is double n ? (int)n : def;
        public static float Num(this Dictionary<string, object> d, string key, float def) => d.TryGetValue(key, out var v) && v is double n ? (float)n : def;
        public static bool Bool(this Dictionary<string, object> d, string key, bool def) => d.TryGetValue(key, out var v) && v is bool b ? b : def;
        public static List<string> Strings(this Dictionary<string, object> d, string key)
        {
            var r = new List<string>();
            if (d.TryGetValue(key, out var v) && v is List<object> l) foreach (var x in l) if (x != null) r.Add(x.ToString());
            return r;
        }
        public static Dictionary<string, object> Obj(this Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
    }
}
