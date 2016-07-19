using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using HabBit.Habbo;

using FlashInspect;
using FlashInspect.ActionScript;

using Sulakore.Protocol.Encryption;

namespace HabBit
{
    public class Program
    {
        static HGame Game { get; set; }
        static HGame PreviousGame { get; set; }

        static string Revision { get; set; }
        static string FileDirectory { get; set; }

        static int Exponent { get; set; } = 3;
        static string Modulus { get; set; } = "86851dd364d5c5cece3c883171cc6ddc5760779b992482bd1e20dd296888df91b33b936a7b93f06d29e8870f703a216257dec7c81de0058fea4cc5116f75e6efc4e9113513e45357dc3fd43d4efab5963ef178b78bd61e81a14c603b24c8bcce0a12230b320045498edc29282ff0603bc7b7dae8fc1b05b52b2f301a9dc783b7";
        static string PrivateExponent { get; set; } = "59ae13e243392e89ded305764bdd9e92e4eafa67bb6dac7e1415e8c645b0950bccd26246fd0d4af37145af5fa026c0ec3a94853013eaae5ff1888360f4f9449ee023762ec195dff3f30ca0b08b8c947e3859877b5d7dced5c8715c58b53740b84e11fbc71349a27c31745fcefeeea57cff291099205e230e0c7c27e8e1c0512b";

        static bool IsDumpingHeaders { get; set; }
        static bool IsUpdatingHeaders { get; set; }
        static bool IsCompressingClient { get; set; }

        static Stopwatch Watch { get; } = new Stopwatch();
        static int UniqueInMessageHashCount { get; set; }
        static int UniqueOutMessageHashCount { get; set; }

        static string OutgoingHeaders { get; set; }
        static string OutgoingHeadersPath { get; set; }

        static string IncomingHeaders { get; set; }
        static string IncomingHeadersPath { get; set; }

        static void Main(string[] args)
        {
            Console.Title = $"HabBit[{GetVersion()}] ~ Processing Arguments...";
            HandleArguments(args);
            UpdateTitle();

            Watch.Restart();
            if (Decompress(Game))
            {
                Game.Disassemble();
                Revision = Game.GetClientRevision();

                UpdateTitle();
                Console.Title += (", Revision: " + Revision);

                if (!FileDirectory.EndsWith(Revision))
                {
                    FileDirectory += ("\\" + Revision);
                    Directory.CreateDirectory(FileDirectory);
                }

                string headerDump = string.Empty;
                if (IsDumpingHeaders)
                {
                    WriteLine($"Generating unique hashes for Outgoing({Game.OutgoingMessages.Count})/Incoming({Game.IncomingMessages.Count}) messages...");
                    headerDump += DumpHeaders(Game, true);
                    headerDump += "\r\n\r\n";
                    headerDump += DumpHeaders(Game, false);
                    WriteLine($"Unique Message Hashes Generated: Outgoing[{UniqueOutMessageHashCount}], Incoming[{UniqueInMessageHashCount}]");

                    if (IsUpdatingHeaders)
                    {
                        WriteLine("Replacing previous Outgoing/Incoming headers with hashes...");
                        PreviousGame.Decompress();
                        PreviousGame.Disassemble();

                        string fileHeader = $"//Current: {Game.GetClientRevision()}\r\n//Previous: {PreviousGame.GetClientRevision()}\r\n";

                        OutgoingHeaders =
                            (fileHeader + UpdateHeaders(
                                OutgoingHeadersPath, Game, PreviousGame, true));

                        IncomingHeaders =
                            (fileHeader + UpdateHeaders(
                                IncomingHeadersPath, Game, PreviousGame, false));
                    }
                }

                Game.BypassOriginCheck();
                Game.BypassRemoteHostCheck();
                Game.ReplaceRSAKeys(Exponent, Modulus);

                WriteLine("Assembling...");
                Game.Assemble();

                byte[] reconstructed = (IsCompressingClient ?
                    Compress(Game) : Game.ToByteArray());

                Watch.Stop();
                WriteLine($"Finished! | Completion Time: {Watch.Elapsed:s\\.ff} Seconds");

                string clientPath = $"{FileDirectory}\\Habbo.swf";
                File.WriteAllBytes(clientPath, reconstructed);

                string rsaKeysPath = $"{FileDirectory}\\RSAKeys.txt";
                File.WriteAllText(rsaKeysPath, string.Format(
                    "Exponent(e): {0:x}\r\nModulus(n): {1}\r\nPrivate Exponent(d): {2}",
                    Exponent, Modulus, PrivateExponent));

                Console.WriteLine("Client: " + clientPath);
                Console.WriteLine("RSA Keys: " + rsaKeysPath);

                if (!string.IsNullOrWhiteSpace(headerDump))
                {
                    string headersPath = $"{FileDirectory}\\Headers.txt";
                    File.WriteAllText(headersPath, headerDump);

                    Console.WriteLine("Headers: " + headersPath);
                    if (IsUpdatingHeaders)
                    {
                        string inPath = $"{FileDirectory}\\{Path.GetFileName(IncomingHeadersPath)}";
                        string outPath = $"{FileDirectory}\\{Path.GetFileName(OutgoingHeadersPath)}";

                        File.WriteAllText(outPath, OutgoingHeaders);
                        Console.WriteLine("Client Outgoing Headers: " + outPath);

                        File.WriteAllText(inPath, IncomingHeaders);
                        Console.WriteLine("Client Incoming Headers: " + inPath);
                    }
                }
                WriteLine();
            }
            else WriteLine($"File decompression failed! | {Game.Compression}");

            Console.CursorVisible = true;
            Console.ReadKey(true);
        }
        static void HandleArguments(string[] args)
        {
            if (args.Length < 1)
                args = GetArguments();

            string path = Path.GetFullPath(args[0]);
            if (!path.EndsWith(".swf") || !File.Exists(path))
                args = GetArguments();

            Game = new HGame(path);
            Game.LoggerCallback = LoggerCallback;
            FileDirectory = Path.GetDirectoryName(path);
            for (int i = 1; i < args.Length; i++)
            {
                string argument = args[i];
                switch (argument.ToLower())
                {
                    case "-updateh":
                    {
                        IsDumpingHeaders = true;
                        IsUpdatingHeaders = true;
                        PreviousGame = new HGame(args[++i]);
                        OutgoingHeadersPath = args[++i];
                        IncomingHeadersPath = args[++i];
                        break;
                    }
                    case "-rsa":
                    {
                        Exponent = Convert.ToInt32(args[++i], 16);
                        Modulus = args[++i];
                        break;
                    }
                    case "-compress":
                    {
                        IsCompressingClient = true;
                        break;
                    }
                    case "-dumph":
                    {
                        IsDumpingHeaders = true;
                        break;
                    }
                }
            }
        }

        static string GetFile()
        {
            do
            {
                Console.Clear();
                Console.Write("Habbo Client Location: ");

                string path = Console.ReadLine();
                if (string.IsNullOrEmpty(path)) continue;

                if (path.StartsWith("\"") && path.EndsWith("\""))
                    path = path.Substring(1, path.Length - 2);

                path = Path.GetFullPath(path);
                if (!File.Exists(path)) continue;

                if (path.EndsWith(".swf"))
                {
                    WriteLine();
                    return path;
                }
            }
            while (true);
        }
        static Version GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }
        static string[] GetArguments()
        {
            var argBuilder = new StringBuilder();
            argBuilder.AppendLine(GetFile());
            if (Ask($"Would you like to use custom RSA keys?"))
            {
                argBuilder.AppendLine("-rsa");
                if (Ask("Would you like to generate new RSA keys?"))
                {
                    var exchange = HKeyExchange.Create(1024);

                    string e = exchange.Exponent.ToString("x");
                    Console.WriteLine("Exponent(e): " + e);
                    argBuilder.AppendLine(e);

                    string n = exchange.Modulus.ToString("x");
                    Console.WriteLine("Modulus(n): " + n);
                    argBuilder.AppendLine(n);

                    PrivateExponent = exchange.PrivateExponent.ToString("x");
                    Console.WriteLine("Private Exponent(d): " + PrivateExponent);
                }
                else
                {
                    PrivateExponent = "<Unknown>";

                    Console.Write("Custom Exponent(e): ");
                    argBuilder.AppendLine(Console.ReadLine());

                    Console.Write("Custom Modulus(n): ");
                    argBuilder.AppendLine(Console.ReadLine());
                }
                Console.WriteLine("---------------");
            }

            if (Ask("Would you like to enable compression?"))
                argBuilder.AppendLine("-compress");

            if (Ask("Would you like to update your Outgoing/Incoming header files?"))
            {
                argBuilder.AppendLine("-updateh");
                Console.CursorVisible = true;

                Console.Write("Previous Client: ");
                argBuilder.AppendLine(Console.ReadLine());

                Console.Write("Outgoing Headers File(Relative to client): ");
                argBuilder.AppendLine(Console.ReadLine());

                Console.Write("Incoming Headers File(Relative to client): ");
                argBuilder.AppendLine(Console.ReadLine());

                Console.CursorVisible = false;
                Console.WriteLine("---------------");
            }
            else if (Ask("Would you like to dump header/message information?"))
                argBuilder.AppendLine("-dumph");

            Console.Clear();
            return argBuilder.ToString().Split(
                new string[] { "\r\n" },
                StringSplitOptions.RemoveEmptyEntries);
        }

        static void UpdateTitle()
        {
            Console.Title =
                $"HabBit[{GetVersion()}] ~ Tags: {Game.Tags.Count:n0}, ABCFiles: {Game.ABCFiles.Count:n0}, Compression: {Game.Compression}";
        }
        static bool Ask(string question)
        {
            try
            {
                Console.CursorVisible = true;
                Console.Write(question + " (Y/N): ");
                while (true)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;
                    bool isYes = (key == ConsoleKey.Y);
                    if (isYes || key == ConsoleKey.N)
                    {
                        WriteLine(key.ToString());
                        return isYes;
                    }
                }
            }
            finally { Console.CursorVisible = false; }
        }
        static void LoggerCallback(string value)
        {
            WriteLine(value);
        }

        static byte[] Compress(ShockwaveFlash flash)
        {
            int uncompressedSizeMB = (((int)flash.FileLength / 1024) / 1024);
            Console.Write($"Compressing... | ({uncompressedSizeMB}MB)");

            byte[] compressedData = flash.Compress();
            int compressedSizeMB = ((compressedData.Length / 1024) / 1024);

            WriteLine($" -> ({compressedSizeMB}MB)");
            return compressedData;
        }
        static bool Decompress(ShockwaveFlash flash)
        {
            if (flash.Compression != CompressionType.None)
            {
                Console.Clear();
                int compressedSizeMB = ((flash.ToByteArray().Length / 1024) / 1024);
                int uncompressedSizeMB = (((int)flash.FileLength / 1024) / 1024);

                Console.Write($"Decompressing... | ({compressedSizeMB}MB)");
                flash.Decompress();
                WriteLine($" -> ({uncompressedSizeMB}MB)");
            }
            return !flash.IsCompressed;
        }

        static string DumpHeaders(HGame game, bool isDumpingOutgoing)
        {
            IReadOnlyDictionary<ushort, ASClass> messageClasses =
                (isDumpingOutgoing ? game.OutgoingMessages : game.IncomingMessages);

            IOrderedEnumerable<KeyValuePair<string, List<ushort>>> organizedHeaders =
                GetOrganizedHeadersByHashCount(game, messageClasses);

            string headersDump = string.Empty;
            string unusedHeadersDump = string.Empty;
            string messageType = (isDumpingOutgoing ? "Outgoing" : "Incoming");
            var unusedHeaders = new List<KeyValuePair<string, List<ushort>>>();
            foreach (KeyValuePair<string, List<ushort>> organizedHeader in organizedHeaders)
            {
                if (organizedHeader.Value.Count == 1)
                {
                    if (isDumpingOutgoing) UniqueOutMessageHashCount++;
                    else UniqueInMessageHashCount++;
                }
                string messageHash = organizedHeader.Key;
                foreach (ushort header in organizedHeader.Value)
                {
                    ASClass messageClass = messageClasses[header];
                    string messageName = messageClass.Instance.QualifiedName.Name;
                    ASMethod messageCtor = messageClass.Instance.Constructor;

                    string dump = $"{messageType}[{header}, {messageHash}] = {messageName}{messageCtor}";
                    if (!isDumpingOutgoing)
                    {
                        ASClass inMsgParser = game.GetIncomingMessageParser(messageClass);
                        dump += ($", Parser: {inMsgParser.Instance.QualifiedName.Name}");
                    }
                    dump += "\r\n";
                    if (!game.IsMessageReferenced(messageClass))
                    {
                        unusedHeadersDump += ("[Dead]" + dump);
                    }
                    else headersDump += dump;
                }
            }

            if (!string.IsNullOrWhiteSpace(unusedHeadersDump))
                headersDump += unusedHeadersDump;

            return headersDump.Trim();
        }
        static string UpdateHeaders(string headersPath, HGame current, HGame previous, bool isUpdatingOutgoing)
        {
            IReadOnlyDictionary<ushort, ASClass> curMsgClasses =
                (isUpdatingOutgoing ? current.OutgoingMessages : current.IncomingMessages);

            IReadOnlyDictionary<ushort, ASClass> preMsgClasses =
                (isUpdatingOutgoing ? previous.OutgoingMessages : previous.IncomingMessages);

            string value = File.ReadAllText(headersPath);
            MatchEvaluator replacer =
                delegate (Match match)
                {
                    bool isOut = isUpdatingOutgoing;
                    string endValue = match.Groups["end"].Value;
                    string headerValue = match.Groups["header"].Value;

                    ushort preHeader = 0;
                    if (!ushort.TryParse(headerValue, out preHeader) ||
                        !preMsgClasses.ContainsKey(preHeader))
                    {
                        if (headerValue != "0000")
                            return $"-1{endValue} //Invalid Header '{headerValue}'";
                        else
                            return ("-1" + endValue);
                    }

                    ASClass msgClass = preMsgClasses[preHeader];
                    string hash = previous.GetMessageHash(msgClass);

                    bool isDead = false;
                    string result = string.Empty;
                    IReadOnlyList<ASClass> curSimilars = current.GetMessages(hash);
                    if (curSimilars == null)
                    {
                        return $"-1{endValue} //No Matches {msgClass.Instance.QualifiedName.Name}[{headerValue}]";
                    }
                    else
                    {
                        ASClass curMsgClass = curSimilars[0];
                        isDead = !current.IsMessageReferenced(curMsgClass);

                        if (curSimilars.Count == 1)
                        {
                            ushort curHeader = current.GetMessageHeader(curMsgClass);
                            result = $"{curHeader}{endValue} //{headerValue}";
                        }
                        else
                        {
                            result = $"-1{endValue} //Duplicate Matches {msgClass.Instance.QualifiedName.Name}[{headerValue}] | {hash}";
                        }
                    }
                    if (isDead)
                    {
                        result +=
                            " | Dead Message(0 References)";
                    }
                    return result;
                };

            value = Regex.Replace(value,
                "( |)//(.*?)\r\n", "\r\n", RegexOptions.Singleline).Trim();

            if (value.Contains("-1"))
            {
                value = Regex.Replace(value,
                    @"-\b1\b", "0000", RegexOptions.Multiline);
            }

            value = Regex.Replace(value,
                @"(\b(?<header>\d{1,4})\b)(?<end>[^\r|$]*)", replacer, RegexOptions.Multiline);

            return value;
        }
        static IOrderedEnumerable<KeyValuePair<string, List<ushort>>> GetOrganizedHeadersByHashCount(HGame game, IReadOnlyDictionary<ushort, ASClass> messageClasses)
        {
            var unorganizedHeaders = new Dictionary<string, List<ushort>>();
            foreach (ushort header in messageClasses.Keys)
            {
                ASClass messageClass = messageClasses[header];
                string messageHash = game.GetMessageHash(messageClass);

                if (!unorganizedHeaders.ContainsKey(messageHash))
                    unorganizedHeaders[messageHash] = new List<ushort>();

                if (!unorganizedHeaders[messageHash].Contains(header))
                    unorganizedHeaders[messageHash].Add(header);
            }
            return unorganizedHeaders.OrderBy(kvp => kvp.Value.Count);
        }

        static void WriteLine()
        {
            WriteLine(string.Empty);
        }
        static void WriteLine(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                value += "\r\n";

            value += "---------------";
            Console.WriteLine(value);
        }
    }
}