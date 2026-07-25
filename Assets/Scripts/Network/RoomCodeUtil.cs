using System;
using System.Text;

/// <summary>
/// 局域网房间短码：Crockford Base32，固定 4 位，大小写不敏感。
/// 短码本身不编码 IP，靠 ManualDiscovery 广播匹配定位主机。
/// </summary>
public static class RoomCodeUtil
{
    public const int CodeLength = 4;

    /// <summary>Crockford Base32（排除 I L O U）。</summary>
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    static readonly System.Random Rng = new System.Random();

    public static string Generate()
    {
        var sb = new StringBuilder(CodeLength);
        lock (Rng)
        {
            for (int i = 0; i < CodeLength; i++)
                sb.Append(Alphabet[Rng.Next(Alphabet.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 规范化并校验短码。成功返回大写 4 位码；失败 out 为 null。
    /// 接受常见混淆：I/L→1，O→0；忽略空白与连字符。
    /// </summary>
    public static bool TryNormalize(string input, out string normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var sb = new StringBuilder(CodeLength);
        foreach (char c in input)
        {
            if (c == '-' || c == ' ' || c == '\t') continue;

            char upper = char.ToUpperInvariant(c);
            switch (upper)
            {
                case 'I':
                case 'L':
                    upper = '1';
                    break;
                case 'O':
                    upper = '0';
                    break;
            }

            if (Alphabet.IndexOf(upper) < 0) return false;
            sb.Append(upper);
        }

        if (sb.Length != CodeLength) return false;
        normalized = sb.ToString();
        return true;
    }
}
