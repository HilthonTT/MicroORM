using System.Text;

namespace MicroORM.Mapping;

internal sealed class SnakeCaseNamingConvention : NamingConvention
{
    public override string Convert(string name)
    {
        ArgumentNullException.ThrowIfNull(name, nameof(name));

        var builder = new StringBuilder();

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                // Break before an uppercase letter that starts a word": "userId" -> "user_id",
                // and before the last capital of an acronym: "HTTPServer -> "http_server".

                char prev = i > 0 ? name[i - 1] : '\0';
                char next = i + 1 < name.Length ? name[i + 1] : '\0';

                bool startsWord = i > 0 && prev != '_' && 
                    (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && char.IsLower(next));

                if (startsWord)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}