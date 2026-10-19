using Sonar.Messages;
using Sonar.Models;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using SonarUtils;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Sonar.Utilities
{
    internal static class IdentifierUtils
    {
        internal const string ClientIdentifierFilename = "identifier";

        internal static ClientIdentifier GetClientIdentifier(SonarStartInfo startInfo)
        {
            var bytes = startInfo.ReadFileBytes(ClientIdentifierFilename, 1024);
            if (bytes is not null)
            {
                try
                {
                    return (ClientIdentifier)SonarSerializer.DeserializeData<ISonarMessage>(bytes);
                }
                catch { /* Swallow */ }
            }
            return new();
        }

        internal static void SaveClientIdentifier(ClientIdentifier identifier, SonarStartInfo startInfo)
        {
            startInfo.WriteFileBytes(ClientIdentifierFilename, identifier.SerializeData());
        }

        public static string? GenerateClientHash(string? clientId, string? clientSecret)
        {
            if (clientId is null || clientSecret is null) return null;
            var bytes = Encoding.UTF8.GetBytes($"{clientId}{clientSecret}");
            var hash = SonarHashing.Sha256(bytes);
            return Base64Url.EncodeToString(hash.AsSpan())[..12];
        }
    }
}
