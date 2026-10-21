using System.Text;

namespace Looppaper.Core.WallpaperEngine;

/// <summary>
/// One node of a Valve KeyValues (VDF) text file: either a key with a string value or a key with
/// child nodes.
/// </summary>
public sealed class KeyValuesNode
{
    public KeyValuesNode(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    /// <summary>The string value, or null when this node is a block of children.</summary>
    public string? Value { get; }

    public List<KeyValuesNode> Children { get; } = [];

    /// <summary>First child with this name (case-insensitive), or null.</summary>
    public KeyValuesNode? this[string name] =>
        Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A small, tolerant parser for Valve KeyValues text such as Steam's <c>libraryfolders.vdf</c>:
/// quoted or bare tokens, <c>{ }</c> blocks, <c>//</c> comments and <c>[$WIN32]</c> conditions.
/// Broken input never throws; whatever could be read is returned.
/// </summary>
public static class KeyValues
{
    /// <summary>Parses the text into a synthetic root node whose children are the top-level keys.</summary>
    public static KeyValuesNode Parse(string text)
    {
        var root = new KeyValuesNode(string.Empty);
        var tokens = Tokenize(text).ToList();
        var index = 0;
        ParseBlock(tokens, ref index, root, topLevel: true);
        return root;
    }

    private static void ParseBlock(List<Token> tokens, ref int index, KeyValuesNode parent, bool topLevel)
    {
        while (index < tokens.Count)
        {
            var token = tokens[index++];
            if (token.Kind == TokenKind.Close)
            {
                if (topLevel)
                {
                    continue; // a stray '}' at the top level is ignored
                }

                return;
            }

            if (token.Kind == TokenKind.Open)
            {
                // A block without a key: keep its contents under an empty name rather than losing them.
                var anonymous = new KeyValuesNode(string.Empty);
                parent.Children.Add(anonymous);
                ParseBlock(tokens, ref index, anonymous, topLevel: false);
                continue;
            }

            var key = token.Text;
            if (index >= tokens.Count)
            {
                parent.Children.Add(new KeyValuesNode(key, string.Empty));
                return;
            }

            var next = tokens[index];
            if (next.Kind == TokenKind.Open)
            {
                index++;
                var child = new KeyValuesNode(key);
                parent.Children.Add(child);
                ParseBlock(tokens, ref index, child, topLevel: false);
            }
            else if (next.Kind == TokenKind.String)
            {
                index++;
                parent.Children.Add(new KeyValuesNode(key, next.Text));
            }
            else
            {
                // "key" followed by '}' : a key without a value.
                parent.Children.Add(new KeyValuesNode(key, string.Empty));
            }
        }
    }

    private enum TokenKind
    {
        String,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static IEnumerable<Token> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '[')
            {
                // Platform condition such as [$WIN32]; Looppaper only runs on Windows, so it is ignored.
                while (i < text.Length && text[i] != ']')
                {
                    i++;
                }

                i++;
            }
            else if (c == '{')
            {
                i++;
                yield return new Token(TokenKind.Open, "{");
            }
            else if (c == '}')
            {
                i++;
                yield return new Token(TokenKind.Close, "}");
            }
            else if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        var e = text[i + 1];
                        sb.Append(e switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => e,
                        });
                        if (e is not ('n' or 't' or '\\' or '"'))
                        {
                            // Unknown escape: keep the backslash too (Windows paths written without escaping).
                            sb.Insert(sb.Length - 1, '\\');
                        }

                        i += 2;
                    }
                    else
                    {
                        sb.Append(text[i++]);
                    }
                }

                i++; // closing quote (or end of text)
                yield return new Token(TokenKind.String, sb.ToString());
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                {
                    i++;
                }

                yield return new Token(TokenKind.String, text[start..i]);
            }
        }
    }
}
