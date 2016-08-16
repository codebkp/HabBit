using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;

using HabBit.Habbo;
using HabBit.Utilities;

using FlashInspect;
using FlashInspect.ActionScript;

using CommandLine;
using CommandLine.Text;

namespace HabBit
{
    public class Program
    {
        private readonly HBRSAKeys _keys;

        private const string DEFAULT_EXPONENT = "3";
        private const string DEFAULT_MODULUS = "86851dd364d5c5cece3c883171cc6ddc5760779b992482bd1e20dd296888df91b33b936a7b93f06d29e8870f703a216257dec7c81de0058fea4cc5116f75e6efc4e9113513e45357dc3fd43d4efab5963ef178b78bd61e81a14c603b24c8bcce0a12230b320045498edc29282ff0603bc7b7dae8fc1b05b52b2f301a9dc783b7";
        private const string DEFAULT_PRIVATE_EXPONENT = "59ae13e243392e89ded305764bdd9e92e4eafa67bb6dac7e1415e8c645b0950bccd26246fd0d4af37145af5fa026c0ec3a94853013eaae5ff1888360f4f9449ee023762ec195dff3f30ca0b08b8c947e3859877b5d7dced5c8715c58b53740b84e11fbc71349a27c31745fcefeeea57cff291099205e230e0c7c27e8e1c0512b";

        public HGame Game { get; }
        public HBOptions Options { get; }

        public string FileName { get; }

        public Program(HBOptions options)
        {
            Options = options;
            Game = new HGame(Options.GamePath);

            if (string.IsNullOrWhiteSpace(Options.OutputPath))
            {
                Options.OutputPath =
                    Path.GetDirectoryName(Options.GamePath);
            }
            else Directory.CreateDirectory(Options.OutputPath);

            if (Options.Compression == null ||
                !Enum.IsDefined(typeof(CompressionType), Options.Compression))
            {
                Options.Compression = Game.Compression;
            }

            if (Options.RSAKeySize != null)
            {
                Console.WriteLine($"Generating {Options.RSAKeySize}-bit RSA Keys...");
                _keys = new HBRSAKeys((int)Options.RSAKeySize);
                WriteLineSplit();
            }
            else if (Options.RSAKeys?.Length >= 2)
            {
                string e = Options.RSAKeys[0];
                string n = Options.RSAKeys[1];
                _keys = new HBRSAKeys(e, n);
            }
            else
            {
                _keys = new HBRSAKeys(DEFAULT_EXPONENT,
                    DEFAULT_MODULUS, DEFAULT_PRIVATE_EXPONENT);
            }

            FileName = Path.GetFileName(Game.Location);
            UpdateTitle(Game);
        }
        public static void Main(string[] args)
        {
            Console.CursorVisible = false;
            try
            {
                var options = new HBOptions();
                bool parsed = Parser.Default.ParseArguments(args, options);

                if (!parsed)
                {

                    var help = HelpText.AutoBuild(options);
                    help.Heading = $"HabBit[Version {GetVersion()}]";
                    help.Copyright = $"Copyright (c) 2016 ArachisH";
                    help.MaximumDisplayWidth = Console.WindowWidth;
                    Console.WriteLine(help);
                }
                else new Program(options).Run();
            }
            finally { Console.CursorVisible = true; }
        }

        public void Run()
        {
            var globalWatch = Stopwatch.StartNew();

            /* Step #1 - Decompression */
            Decompress();

            /* Step #2 - Disassembling */
            Disassemble();

            /* Step #2.5 - Modification */
            Modify();

            /* Step #3 - Compression/Assembling */
            Assemble();

            Console.WriteLine("Cleaning Up...");
            Console.WriteLine();

            Console.WriteLine("    Saving RSA Keys...");
            using (var rsaKeyWriter = new StreamWriter(
                Path.Combine(Options.OutputPath, "RSAKeys.txt")))
            {
                rsaKeyWriter.WriteLine("Exponent(e): {0}", _keys.Exponent);
                rsaKeyWriter.WriteLine("Modulus(n): {0}", _keys.Modulus);
                rsaKeyWriter.WriteLine("Private Exponent(d): {0}", _keys.PrivateExponent ?? "<Unknown>");
            }

            if (Options.IsDumpingHeaders)
            {
                Console.WriteLine("    Dumping Headers...");
                using (var headersWriter = new StreamWriter(
                    Path.Combine(Options.OutputPath, "Headers.txt")))
                {
                    headersWriter.WriteLine("// Outgoing Messages | {0:n0}", Game.OutMessages.Count);
                    WriteMessages("Outgoing", Game.OutMessages, headersWriter);

                    headersWriter.WriteLine();

                    headersWriter.WriteLine("// Incoming Messages | {0:n0}", Game.InMessages.Count);
                    WriteMessages("Incoming", Game.InMessages, headersWriter);
                }
            }

            WriteLineSplit();

            globalWatch.Stop();
            Console.WriteLine("Completion Time: {0:s\\.ff} Seconds", globalWatch.Elapsed);
        }
        public void Modify()
        {
            Console.WriteLine("Modifying...");
            Console.WriteLine();

            if (!string.IsNullOrWhiteSpace(Options.Revision))
            {
                Game.Revision = Options.Revision;

                Console.WriteLine(
                    $"    Revision Changed: \"{Game.Revision}\"");
            }

            bool replacedPatterns = false;
            if (Options.Patterns != null)
            {
                for (int i = 0, j = 0; i < Game.ValidHostsRegexPatterns.Length; i++)
                {
                    if (j >= Options.Patterns.Length) break;
                    string newPattern = Options.Patterns[j++];
                    if (string.IsNullOrWhiteSpace(newPattern))
                    {
                        i--;
                        continue;
                    }

                    replacedPatterns = true;
                    Game.ValidHostsRegexPatterns[i] = newPattern;

                    Console.WriteLine(
                        $"    Valid Host Pattern Changed[{i}]: \"{newPattern}\"");
                }
            }

            Console.Write("    Replacing RSA Keys...");
            Game.ReplaceRSAKeys(_keys.Exponent, _keys.Modulus).WriteLineResult();

            Console.Write("    Bypassing Domain Checks...");
            Game.BypassDomainChecks(replacedPatterns).WriteLineResult();

            WriteLineSplit();
        }
        public void Assemble()
        {
            Console.WriteLine("Assembling...");
            Game.Assemble();
            WriteLineSplit();

            byte[] compressed = null;
            if (Options.Compression != null &&
                Options.Compression != CompressionType.None)
            {
                Game.Compression =
                    (CompressionType)Options.Compression;

                Console.WriteLine("Compressing({0})...", Game.Compression);
                compressed = Game.Compress();
                WriteLineSplit();
            }

            string assembledPath = Path.Combine(
                Options.OutputPath, "asmd_" + FileName);

            File.WriteAllBytes(assembledPath,
                compressed ?? Game.ToArray());
        }
        public void Decompress()
        {
            if (Game.IsCompressed)
            {
                Console.WriteLine("Decompressing({0})...", Game.Compression);
                Game.Decompress();
                WriteLineSplit();
            }
        }
        public void Disassemble()
        {
            Console.WriteLine("Disassembling...");
            Game.Disassemble();
            WriteLineSplit();
        }

        private void WriteSplit()
        {
            Console.Write("---------------");
        }
        private void WriteLineSplit()
        {
            WriteSplit();
            Console.WriteLine();
        }
        private void WriteMessages(string title, IReadOnlyDictionary<ushort, ASClass> messages, StreamWriter writer)
        {
            foreach (ushort header in messages.Keys)
            {
                ASClass msgClass = messages[header];
                ASInstance msgInstance = msgClass.Instance;
                string msgName = msgInstance.QName.Name;

                writer.WriteLine("{0}[{1}] = {2}",
                    title, header, msgName);
            }
        }

        private static Version GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }
        private static double BytesToMB(long bytes)
        {
            return (bytes / 1024F) / 1024F;
        }
        private static void UpdateTitle(ShockwaveFlash flash)
        {
            var titleBuilder = new StringBuilder();
            titleBuilder.Append(" | ");
            titleBuilder.Append(Path.GetFileName(flash.Location));
            titleBuilder.AppendFormat("({0:00.0} MB)", BytesToMB(flash.FileLength));
            Console.Title += titleBuilder;
        }
    }
}