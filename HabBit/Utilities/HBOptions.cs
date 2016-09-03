using CommandLine;

using FlashInspect;

namespace HabBit.Utilities
{
    public class HBOptions
    {
        [Option('g',
            Required = true,
            HelpText = "The game client file path to modify.",
            MetaValue = "<path>")]
        public string GamePath { get; set; }

        [Option('c',
            HelpText = "Override compression type to use after the game client has been assembled.",
            MetaValue = "<none|zlib|lzma>")]
        public CompressionType? Compression { get; set; }

        [Option('o',
            HelpText = "The output path for the re-assembled game client, and other resources.",
            MetaValue = "<path>")]
        public string OutputPath { get; set; }

        [Option("rev",
            HelpText = "  Overrides the revision value found in the client's Outgoing[4000] message class.",
            MetaValue = "\b <revision>")]
        public string Revision { get; set; }

        [Option("dhead",
            HelpText = "Dump all Outgoing/Incoming message headers.",
            DefaultValue = false)]
        public bool IsDumpingHeaders { get; set; }

        [Option("dhand",
            HelpText = "Disables the handshake process within the client, data encryption will also be disabled due to this.",
            DefaultValue = false)]
        public bool IsDisablingHandshake { get; set; }

        [OptionArray("rsa",
            HelpText = "  Overrides the client's public RSA keys with the ones provided. The provided keys should be in base-16(Hexadecimal).",
            MetaValue = "\b <exponent> <modulus>")]
        public string[] RSAKeys { get; set; }

        [Option("genrsa",
            HelpText = "  Creates a fresh batch of RSA keys(with private key), this flag will override the '--rsa' argument.",
            MetaValue = "\b <keySize>")]
        public int? RSAKeySize { get; set; }

        [Option("fixiden",
            HelpText = "Fixes every corrupted class/namespace/symbol name.")]
        public bool IsFixingIdentifiers { get; set; }

        [Option("renreg",
            HelpText = "Renames every Debug's instruction register name in every method body to a unique value.")]
        public bool IsRenamingRegisters { get; set; }

        [OptionArray("patt",
            HelpText = "  Replaces the regex patterns found in the main Habbo class that validate where the client is being hosted from.",
            MetaValue = "\b <pattern1> <pattern...>")]
        public string[] Patterns { get; set; }
    }
}