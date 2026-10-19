using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Sonar.Messages;

namespace Sonar.Models
{
    [MessagePackObject]
    public sealed class HardwareIdentifier : ISonarMessage
    {
        public HardwareIdentifier() { }

        [Key(0)]
        public string? Identifier { get; set; }
    }
}
