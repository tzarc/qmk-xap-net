// Copyright 2026 QMK Collaborators
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Xap.SourceGenerator.Models;

// JsonSpecLoader and the model types it returns are internal by design (this is a
// source-generator project, not a public API). Xap.Tests needs to call JsonSpecLoader.Read and
// inspect the resulting XapSpecModel/RouteNode tree directly rather than reimplementing the
// parsing logic under test, so it needs internals access.
[assembly: InternalsVisibleTo("Xap.Tests")]

namespace Xap.SourceGenerator.Helpers;

/// <summary>
/// Reads a XAP spec JSON document into the typed <see cref="XapSpecModel"/>.
/// Single-pass design: the whole document is tokenized/parsed exactly once into an
/// in-memory <see cref="JsonValue"/> tree, which is then projected into the typed
/// model. There is no second parse from string offsets.
/// </summary>
internal static class JsonSpecLoader
{
    public static XapSpecModel Read(string json)
    {
        JsonValue root = JsonParser.Parse(json);

        return new XapSpecModel
        {
            Version = root.GetString("version"),
            HasResponseFlags = root.GetObjectOrNull("response_flags") is not null,
            Routes = ProjectRoutes(root.GetObjectOrNull("routes")),
            BroadcastMessages = ProjectBroadcasts(root.GetObjectOrNull("broadcast_messages")),
        };
    }

    private static Dictionary<string, RouteNode> ProjectRoutes(JsonValue? routesObj)
    {
        var result = new Dictionary<string, RouteNode>();
        if (routesObj is null)
            return result;

        foreach ((string? key, JsonValue? val) in routesObj.Object!)
            result[key] = ProjectRoute(key, val);

        return result;
    }

    private static RouteNode ProjectRoute(string id, JsonValue obj)
    {
        bool isRouter = obj.GetString("type") == "router";

        var node = new RouteNode
        {
            Id = id,
            IsRouter = isRouter,
            Define = obj.GetString("define"),
            Permissions = obj.GetString("permissions"),
            ReturnType = obj.GetString("return_type"),
            ReturnPurpose = obj.GetString("return_purpose"),
            RequestType = obj.GetString("request_type"),
            ReturnStructMembers = ProjectStructMembers(obj.GetArrayOrNull("return_struct_members")),
            RequestStructMembers = ProjectStructMembers(obj.GetArrayOrNull("request_struct_members")),
        };

        if (isRouter && obj.GetObjectOrNull("routes") is { } childRoutes)
        {
            foreach ((string? childId, JsonValue? childVal) in childRoutes.Object!)
                node.Routes[childId] = ProjectRoute(childId, childVal);
        }

        return node;
    }

    private static List<StructMember> ProjectStructMembers(JsonValue? array)
    {
        var result = new List<StructMember>();
        if (array is null)
            return result;

        foreach (JsonValue item in array.Array!)
        {
            result.Add(new StructMember
            {
                Type = item.GetString("type"),
                Name = item.GetString("name"),
            });
        }

        return result;
    }

    private static BroadcastMessagesModel? ProjectBroadcasts(JsonValue? obj)
    {
        if (obj is null)
            return null;

        var messages = new Dictionary<string, BroadcastMessage>();
        if (obj.GetObjectOrNull("messages") is { } messagesObj)
        {
            foreach ((string? key, JsonValue? val) in messagesObj.Object!)
            {
                messages[key] = new BroadcastMessage
                {
                    Id = key,
                    Define = val.GetString("define"),
                };
            }
        }

        return new BroadcastMessagesModel
        {
            Messages = messages,
        };
    }

    /// <summary>
    /// A parsed-once JSON value. Objects and arrays hold already-parsed <see cref="JsonValue"/>
    /// children, never raw string offsets.
    /// </summary>
    private sealed class JsonValue
    {
        public JsonKind Kind;
        public string? String;
        public Dictionary<string, JsonValue>? Object;
        public List<JsonValue>? Array;

        public string GetString(string key)
            => Kind == JsonKind.Object && Object!.TryGetValue(key, out JsonValue? v) && v.Kind == JsonKind.String
                ? v.String!
                : "";

        public JsonValue? GetObjectOrNull(string key)
            => Kind == JsonKind.Object && Object!.TryGetValue(key, out JsonValue? v) && v.Kind == JsonKind.Object
                ? v
                : null;

        public JsonValue? GetArrayOrNull(string key)
            => Kind == JsonKind.Object && Object!.TryGetValue(key, out JsonValue? v) && v.Kind == JsonKind.Array
                ? v
                : null;
    }

    private enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        True,
        False,
        Null,
    }

    /// <summary>
    /// Tokenizing recursive-descent parser. Parses the whole document exactly once into a
    /// <see cref="JsonValue"/> tree; nothing downstream re-reads the source string.
    /// </summary>
    private sealed class JsonParser
    {
        private readonly string _text;
        private int _pos;

        private JsonParser(string text)
        {
            _text = text;
        }

        public static JsonValue Parse(string text)
        {
            var parser = new JsonParser(text);
            parser.SkipWhitespace();
            JsonValue value = parser.ParseValue();
            parser.SkipWhitespace();
            return value;
        }

        private JsonValue ParseValue()
        {
            SkipWhitespace();
            return IsEnd
                ? throw new FormatException("Unexpected end of JSON input.")
                : Current switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => new JsonValue { Kind = JsonKind.String, String = ParseString() },
                    't' => ParseLiteral("true", JsonKind.True),
                    'f' => ParseLiteral("false", JsonKind.False),
                    'n' => ParseLiteral("null", JsonKind.Null),
                    _ => ParseNumber(),
                };
        }

        private JsonValue ParseObject()
        {
            var result = new Dictionary<string, JsonValue>();
            Expect('{');
            SkipWhitespace();

            if (!IsEnd && Current == '}')
            {
                Advance();
                return new JsonValue { Kind = JsonKind.Object, Object = result };
            }

            while (true)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();
                Expect(':');
                JsonValue value = ParseValue();
                result[key] = value;

                SkipWhitespace();
                if (!IsEnd && Current == ',')
                {
                    Advance();
                    SkipWhitespace();
                    continue;
                }
                break;
            }

            SkipWhitespace();
            Expect('}');
            return new JsonValue { Kind = JsonKind.Object, Object = result };
        }

        private JsonValue ParseArray()
        {
            var result = new List<JsonValue>();
            Expect('[');
            SkipWhitespace();

            if (!IsEnd && Current == ']')
            {
                Advance();
                return new JsonValue { Kind = JsonKind.Array, Array = result };
            }

            while (true)
            {
                JsonValue value = ParseValue();
                result.Add(value);

                SkipWhitespace();
                if (!IsEnd && Current == ',')
                {
                    Advance();
                    continue;
                }
                break;
            }

            SkipWhitespace();
            Expect(']');
            return new JsonValue { Kind = JsonKind.Array, Array = result };
        }

        private string ParseString()
        {
            Expect('"');
            var sb = new StringBuilder();

            while (!IsEnd && Current != '"')
            {
                char c = Current;
                if (c == '\\')
                {
                    Advance();
                    if (IsEnd)
                        throw new FormatException("Unterminated string escape.");
                    char esc = Current;
                    switch (esc)
                    {
                        case '"':
                            sb.Append('"');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '/':
                            sb.Append('/');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            string hex = _text.Substring(_pos + 1, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                    Advance();
                }
                else
                {
                    sb.Append(c);
                    Advance();
                }
            }

            Expect('"');
            return sb.ToString();
        }

        private JsonValue ParseNumber()
        {
            int start = _pos;
            if (!IsEnd && Current == '-')
                Advance();
            while (!IsEnd && (char.IsDigit(Current) || Current is '.' or 'e' or 'E' or '+' or '-'))
                Advance();

            return _pos == start
                ? throw new FormatException($"Unexpected character: {(IsEnd ? "<eof>" : Current.ToString())}")
                : new JsonValue { Kind = JsonKind.Number, String = _text.Substring(start, _pos - start) };
        }

        private JsonValue ParseLiteral(string literal, JsonKind kind)
        {
            if (_pos + literal.Length > _text.Length || _text.Substring(_pos, literal.Length) != literal)
                throw new FormatException($"Unexpected character: {Current}");

            _pos += literal.Length;
            return new JsonValue { Kind = kind };
        }

        private void SkipWhitespace()
        {
            while (!IsEnd && char.IsWhiteSpace(Current))
                Advance();
        }

        private void Expect(char expected)
        {
            if (IsEnd || Current != expected)
                throw new FormatException($"Expected '{expected}' but found '{(IsEnd ? "<eof>" : Current.ToString())}' at position {_pos}.");
            Advance();
        }

        private bool IsEnd => _pos >= _text.Length;
        private char Current => _text[_pos];
        private void Advance() => _pos++;
    }
}
