#if !GLOADER_SERVER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

internal static class MiniJson
{
    public static object Parse(string text)
    {
        return new Parser(text ?? string.Empty).ParseValue();
    }

    public static string Stringify(object value)
    {
        var builder = new StringBuilder();
        WriteValue(builder, value);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, object value)
    {
        if (value == null) { builder.Append("null"); return; }
        if (value is string s) { WriteString(builder, s); return; }
        if (value is bool b) { builder.Append(b ? "true" : "false"); return; }
        if (value is int || value is long || value is float || value is double || value is decimal)
        {
            builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            return;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            builder.Append('{');
            var first = true;
            foreach (var pair in dictionary)
            {
                if (!first) builder.Append(',');
                first = false;
                WriteString(builder, pair.Key);
                builder.Append(':');
                WriteValue(builder, pair.Value);
            }
            builder.Append('}');
            return;
        }

        if (value is System.Collections.IEnumerable enumerable)
        {
            builder.Append('[');
            var first = true;
            foreach (var item in enumerable)
            {
                if (!first) builder.Append(',');
                first = false;
                WriteValue(builder, item);
            }
            builder.Append(']');
            return;
        }

        WriteString(builder, value.ToString());
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 32) builder.Append("\\u" + ((int)c).ToString("x4"));
                    else builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;

        public Parser(string text) { _text = text; }

        public object ParseValue()
        {
            SkipWhitespace();
            if (_index >= _text.Length) return null;
            switch (_text[_index])
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': ReadLiteral("true"); return true;
                case 'f': ReadLiteral("false"); return false;
                case 'n': ReadLiteral("null"); return null;
                default: return ParseNumber();
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Expect('{');
            SkipWhitespace();
            if (TryConsume('}')) return result;
            while (true)
            {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                result[key] = ParseValue();
                SkipWhitespace();
                if (TryConsume('}')) break;
                Expect(',');
            }
            return result;
        }

        private List<object> ParseArray()
        {
            var result = new List<object>();
            Expect('[');
            SkipWhitespace();
            if (TryConsume(']')) return result;
            while (true)
            {
                result.Add(ParseValue());
                SkipWhitespace();
                if (TryConsume(']')) break;
                Expect(',');
            }
            return result;
        }

        private string ParseString()
        {
            Expect('"');
            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                var c = _text[_index++];
                if (c == '"') return builder.ToString();
                if (c != '\\') { builder.Append(c); continue; }
                if (_index >= _text.Length) throw new InvalidDataException("Invalid JSON escape.");
                c = _text[_index++];
                switch (c)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_index + 4 > _text.Length) throw new InvalidDataException("Invalid JSON unicode escape.");
                        var hex = _text.Substring(_index, 4);
                        _index += 4;
                        builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    default: throw new InvalidDataException("Invalid JSON escape: \\" + c);
                }
            }
            throw new InvalidDataException("Unterminated JSON string.");
        }

        private object ParseNumber()
        {
            var start = _index;
            while (_index < _text.Length)
            {
                var c = _text[_index];
                if (!(char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')) break;
                _index++;
            }
            if (start == _index) throw new InvalidDataException("Unexpected JSON token at " + _index + ".");
            var raw = _text.Substring(start, _index - start);
            long integer;
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
            double floating;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out floating)) return floating;
            throw new InvalidDataException("Invalid JSON number: " + raw);
        }

        private void ReadLiteral(string literal)
        {
            if (_index + literal.Length > _text.Length ||
                !string.Equals(_text.Substring(_index, literal.Length), literal, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid JSON literal.");
            _index += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
        }

        private bool TryConsume(char expected)
        {
            if (_index < _text.Length && _text[_index] == expected) { _index++; return true; }
            return false;
        }

        private void Expect(char expected)
        {
            if (_index >= _text.Length || _text[_index] != expected)
                throw new InvalidDataException("Expected '" + expected + "' at JSON offset " + _index + ".");
            _index++;
        }
    }
}

internal static class JsonValue
{
    public static Dictionary<string, object> Object(object value) => value as Dictionary<string, object>;
    public static List<object> Array(object value) => value as List<object>;

    public static string String(IDictionary<string, object> obj, string key, string fallback = "")
    {
        object value;
        if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
    }

    public static int Int(IDictionary<string, object> obj, string key, int fallback = 0)
    {
        object value;
        if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    public static bool Bool(IDictionary<string, object> obj, string key, bool fallback = false)
    {
        object value;
        if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
        try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    public static Dictionary<string, object> ChildObject(IDictionary<string, object> obj, string key)
    {
        object value;
        return obj != null && obj.TryGetValue(key, out value) ? Object(value) : null;
    }

    public static List<object> ChildArray(IDictionary<string, object> obj, string key)
    {
        object value;
        return obj != null && obj.TryGetValue(key, out value) ? Array(value) : null;
    }
}
#endif
