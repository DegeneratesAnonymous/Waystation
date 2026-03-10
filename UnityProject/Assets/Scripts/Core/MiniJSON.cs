// MiniJSON — lightweight JSON parser/serialiser bundled with the project.
// Source: https://github.com/zanders3/json (MIT Licence)
// This single-file library provides MiniJSON.Json.Deserialize and .Serialize
// without any Unity-specific dependencies, making it suitable for use in
// both Editor and Runtime contexts.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiniJSON
{
    public static class Json
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            return new Parser(json).Parse();
        }

        public static string Serialize(object obj)
        {
            return Serializer.Serialize(obj);
        }

        private sealed class Parser : IDisposable
        {
            private const string WordBreak = "{}[],:\"";
            private StringReader json;

            internal Parser(string jsonString) { json = new StringReader(jsonString); }

            public void Dispose() { json.Dispose(); }

            public object Parse()
            {
                switch (NextToken)
                {
                    case TOKEN.CURLY_OPEN:  return ParseObject();
                    case TOKEN.SQUARED_OPEN:return ParseArray();
                    case TOKEN.STRING:      return ParseString();
                    case TOKEN.NUMBER:      return ParseNumber();
                    case TOKEN.TRUE:        return true;
                    case TOKEN.FALSE:       return false;
                    case TOKEN.NULL:        return null;
                    default:               return null;
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var table = new Dictionary<string, object>();
                while (true)
                {
                    switch (NextToken)
                    {
                        case TOKEN.NONE:       return null;
                        case TOKEN.CURLY_CLOSE:return table;
                        default:
                            string key = ParseString();
                            if (NextToken != TOKEN.COLON) return null;
                            json.Read();
                            table[key] = Parse();
                            break;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var array = new List<object>();
                while (true)
                {
                    switch (NextToken)
                    {
                        case TOKEN.NONE:         return null;
                        case TOKEN.SQUARED_CLOSE:return array;
                        default:
                            array.Add(Parse());
                            break;
                    }
                }
            }

            private string ParseString()
            {
                var s = new StringBuilder();
                json.Read();   // consume opening "
                while (true)
                {
                    if (json.Peek() == -1) break;
                    char c = NextChar;
                    switch (c)
                    {
                        case '"': return s.ToString();
                        case '\\':
                            if (json.Peek() == -1) break;
                            c = NextChar;
                            switch (c)
                            {
                                case '"':  s.Append('"');  break;
                                case '\\': s.Append('\\'); break;
                                case '/':  s.Append('/');  break;
                                case 'b':  s.Append('\b'); break;
                                case 'f':  s.Append('\f'); break;
                                case 'n':  s.Append('\n'); break;
                                case 'r':  s.Append('\r'); break;
                                case 't':  s.Append('\t'); break;
                                case 'u':
                                    var hex = new StringBuilder();
                                    for (int i = 0; i < 4; i++) hex.Append(NextChar);
                                    s.Append((char)Convert.ToInt32(hex.ToString(), 16));
                                    break;
                            }
                            break;
                        default:
                            s.Append(c);
                            break;
                    }
                }
                return s.ToString();
            }

            private object ParseNumber()
            {
                string num = NextWord;
                if (num.IndexOf('.') == -1 && num.IndexOf('e') == -1 && num.IndexOf('E') == -1)
                {
                    if (long.TryParse(num, out long l)) return l;
                }
                if (double.TryParse(num, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
                return 0;
            }

            private void EatWhitespace()
            {
                while (char.IsWhiteSpace((char)json.Peek())) json.Read();
            }

            private char NextChar => (char)json.Read();

            private string NextWord
            {
                get
                {
                    var word = new StringBuilder();
                    while (WordBreak.IndexOf((char)json.Peek()) == -1 && json.Peek() != -1)
                        word.Append(NextChar);
                    return word.ToString();
                }
            }

            private enum TOKEN
            {
                NONE, CURLY_OPEN, CURLY_CLOSE, SQUARED_OPEN, SQUARED_CLOSE,
                COLON, COMMA, STRING, NUMBER, TRUE, FALSE, NULL
            }

            private TOKEN NextToken
            {
                get
                {
                    EatWhitespace();
                    if (json.Peek() == -1) return TOKEN.NONE;
                    switch ((char)json.Peek())
                    {
                        case '{': return TOKEN.CURLY_OPEN;
                        case '}': json.Read(); return TOKEN.CURLY_CLOSE;
                        case '[': return TOKEN.SQUARED_OPEN;
                        case ']': json.Read(); return TOKEN.SQUARED_CLOSE;
                        case ',': json.Read(); return TOKEN.COMMA;
                        case '"': return TOKEN.STRING;
                        case ':': return TOKEN.COLON;
                        case '0': case '1': case '2': case '3': case '4':
                        case '5': case '6': case '7': case '8': case '9':
                        case '-': return TOKEN.NUMBER;
                        default:
                            string word = NextWord;
                            switch (word)
                            {
                                case "false":return TOKEN.FALSE;
                                case "true": return TOKEN.TRUE;
                                case "null": return TOKEN.NULL;
                            }
                            return TOKEN.NONE;
                    }
                }
            }
        }

        private sealed class Serializer
        {
            private StringBuilder builder;

            private Serializer() { builder = new StringBuilder(); }

            public static string Serialize(object obj)
            {
                var s = new Serializer();
                s.SerializeValue(obj);
                return s.builder.ToString();
            }

            private void SerializeValue(object value)
            {
                if (value == null)              { builder.Append("null"); return; }
                if (value is string str)        { SerializeString(str);   return; }
                if (value is bool b)            { builder.Append(b ? "true" : "false"); return; }
                if (value is IDictionary dict)  { SerializeObject(dict);  return; }
                if (value is IList list)        { SerializeArray(list);   return; }
                builder.Append(Convert.ToString(value,
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            private void SerializeObject(IDictionary dict)
            {
                builder.Append('{');
                bool first = true;
                foreach (object key in dict.Keys)
                {
                    if (!first) builder.Append(',');
                    SerializeString(key.ToString());
                    builder.Append(':');
                    SerializeValue(dict[key]);
                    first = false;
                }
                builder.Append('}');
            }

            private void SerializeArray(IList list)
            {
                builder.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) builder.Append(',');
                    SerializeValue(item);
                    first = false;
                }
                builder.Append(']');
            }

            private void SerializeString(string str)
            {
                builder.Append('"');
                foreach (char c in str)
                {
                    switch (c)
                    {
                        case '"':  builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b");  break;
                        case '\f': builder.Append("\\f");  break;
                        case '\n': builder.Append("\\n");  break;
                        case '\r': builder.Append("\\r");  break;
                        case '\t': builder.Append("\\t");  break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4"));
                            }
                            else builder.Append(c);
                            break;
                    }
                }
                builder.Append('"');
            }
        }
    }
}
