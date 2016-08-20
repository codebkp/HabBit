using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using FlashInspect;
using FlashInspect.IO;
using FlashInspect.Tags;
using FlashInspect.Records;
using FlashInspect.ActionScript;
using FlashInspect.ActionScript.Traits;
using FlashInspect.ActionScript.Instructions;
using FlashInspect.ActionScript.Multinames;

namespace HabBit.Habbo
{
    public class HGame : ShockwaveFlash
    {
        private ASMethod _validHostsRegexChecker;

        private readonly string[] _reservedNames;
        private readonly IList<DoABCTag> _abcTags;
        private readonly IDictionary<DoABCTag, ABCFile> _abcFiles;
        private readonly SortedDictionary<ushort, ASClass> _outMessages, _inMessages;

        // There are only four patterns as of 08/08/2016.
        private const int MAX_REGEX_PATTERNS = 4;

        public ReadOnlyDictionary<ushort, ASClass> InMessages { get; }
        public ReadOnlyDictionary<ushort, ASClass> OutMessages { get; }

        private int _revisionIndex;
        public string Revision
        {
            get
            {
                return GetABCFile(2)?.
                    Pool.Strings[_revisionIndex];
            }
            set
            {
                GetABCFile(2)?.
                  Pool.SetString(_revisionIndex, value);
            }
        }

        // These will be set in their respective constant pool prior to being assembled.
        private readonly int[] _validHostsRegexPatternsIndices;
        public string[] ValidHostsRegexPatterns { get; }

        public HGame(string path)
            : this(File.ReadAllBytes(path))
        {
            Location = Path.GetFullPath(path);
        }
        public HGame(byte[] data)
            : base(data)
        {
            _reservedNames = new string[]
            {
                "if",
                "do",
                "get",
                "for",
                "false",
                "true",
                "while",
                "package"
            };

            _abcTags = new List<DoABCTag>(3);
            _abcFiles = new Dictionary<DoABCTag, ABCFile>(3);

            _inMessages = new SortedDictionary<ushort, ASClass>();
            InMessages = new ReadOnlyDictionary<ushort, ASClass>(_inMessages);

            _outMessages = new SortedDictionary<ushort, ASClass>();
            OutMessages = new ReadOnlyDictionary<ushort, ASClass>(_outMessages);

            _validHostsRegexPatternsIndices = new int[MAX_REGEX_PATTERNS];
            ValidHostsRegexPatterns = new string[MAX_REGEX_PATTERNS];
        }

        public bool BypassDomainChecks()
        {
            return BypassDomainChecks(false);
        }
        public bool BypassDomainChecks(bool avoidHostRegexChecks)
        {
            var domainChecks = new List<ASMethod>(2);

            ABCFile abc = GetABCFile(0);
            if (abc == null) return false;

            ASMethod isLocalHostCheck = GetABCFile(0)?.Classes?[0]
                .GetMethods(1, "Boolean").FirstOrDefault();

            if (isLocalHostCheck != null)
                domainChecks.Add(isLocalHostCheck);

            if (!avoidHostRegexChecks &&
                _validHostsRegexChecker != null)
            {
                domainChecks.Add(_validHostsRegexChecker);
            }

            if (domainChecks.Count > 0)
            {
                foreach (ASMethod domainCheck in domainChecks)
                {
                    byte[] code = domainCheck.Body.Code;
                    if (!code.StartsWith(Operation.PushTrue, Operation.ReturnValue))
                    {
                        code = code.Insert(0, Operation.PushTrue, Operation.ReturnValue);
                        domainCheck.Body.Code = code;
                    }
                }
            }

            return (domainChecks.Count > 0 &&
                DisableHostPrepender());
        }

        public void FixRegisters()
        {
            for (int i = 0; i < 3; i++)
            {
                ABCFile abc = GetABCFile(i);
                var localNameIndices = new Dictionary<string, int>();
                foreach (ASMethodBody body in abc.MethodBodies)
                {
                    // Can't fix jump instructions inside try/catch scopes, yet.
                    if (body.Exceptions.Count > 0) continue;
                    AS3Code code = body.ParseCode();

                    bool hasSwitch = false;
                    foreach (Instruction ins in code.Instructions)
                    {
                        if (ins.OP == Operation.Debug)
                        {
                            var debugIns = (DebugIns)ins;
                            string localName = ("local" + debugIns.RegisterIndex);

                            int localNameIndex = 0;
                            if (!localNameIndices.TryGetValue(
                                localName, out localNameIndex))
                            {
                                localNameIndex = abc.Pool.AddString(localName);
                                localNameIndices[localName] = localNameIndex;
                            }

                            debugIns.NameIndex = localNameIndex;
                        }
                        else if (ins.OP == Operation.LookUpSwitch)
                        {
                            // Or switch statements..
                            hasSwitch = true;
                            break;
                        }
                    }

                    if (!hasSwitch)
                        body.Code = code.ToArray();
                }
            }
        }
        public void FixIdentifiers()
        {
            int invalidInstanceCount = 0;
            // [qName][fixedName]
            var fixedNS = new SortedDictionary<string, string>();
            // [qName][namespace][fixedName]
            var fixedIN = new SortedDictionary<string, SortedDictionary<string, string>>();
            for (int i = 0; i < 3; i++)
            {
                ABCFile abc = GetABCFile(i);
                ASConstantPool pool = abc.Pool;
                try
                {
                    // We've implemented our own cache system.
                    pool.RecycleIndices = false;

                    #region Namespace Fixing
                    // Several types of namespaces that share the same name.
                    // private, protected, blah blah, cache the name index.
                    var fixedNSIndices = new Dictionary<string, int>();
                    for (int j = 1; j < pool.Namespaces.Count; j++)
                    {
                        ASNamespace @namespace = pool.Namespaces[j];
                        if (@namespace.Name.StartsWith("_-") ||
                            _reservedNames.Contains(@namespace.Name.Trim()))
                        {
                            string fixedName = string.Empty;
                            if (!fixedNS.TryGetValue(@namespace.Name, out fixedName))
                            {
                                fixedName = ("ns_" + (fixedNS.Count + 1));
                                fixedNS.Add(@namespace.Name, fixedName);
                            }

                            int fixedNameIndex = 0;
                            if (!fixedNSIndices.TryGetValue(fixedName, out fixedNameIndex))
                            {
                                fixedNameIndex = abc.Pool.AddString(fixedName);
                                fixedNSIndices.Add(fixedName, fixedNameIndex);
                            }
                            @namespace.NameIndex = fixedNameIndex;
                        }
                    }
                    #endregion

                    #region Class/Instance Fixing
                    var fixedINIndices = new Dictionary<string, int>();
                    for (int j = 0; j < abc.Instances.Count; j++)
                    {
                        ASInstance instance = abc.Instances[j];
                        var qName = (QName)instance.QName.Data;

                        if (qName.Name.StartsWith("_-") ||
                            _reservedNames.Contains(qName.Name.Trim()))
                        {
                            SortedDictionary<string, string> fixedNames = null;
                            string fixedName = ("class_" + (++invalidInstanceCount));
                            if (!fixedIN.TryGetValue(qName.Name, out fixedNames))
                            {
                                fixedNames = new SortedDictionary<string, string>();
                                fixedIN.Add(qName.Name, fixedNames);
                            }
                            fixedNames.Add(qName.Namespace.Name, fixedName);

                            int fixedNameIndex = 0;
                            if (!fixedINIndices.TryGetValue(fixedName, out fixedNameIndex))
                            {
                                fixedNameIndex = abc.Pool.AddString(fixedName);
                                fixedINIndices.Add(fixedName, fixedNameIndex);
                            }
                            qName.NameIndex = fixedNameIndex;
                        }
                    }
                    #endregion

                    // TODO: Trait name fixing.

                    #region Multiname Fixing (Shared Classes/Instances, Traits(soon))
                    for (int j = 1; j < pool.Multinames.Count; j++)
                    {
                        ASMultiname multiname = pool.Multinames[j];
                        IMultiname data = multiname.Data;

                        SortedDictionary<string, string> fixedNames = null;
                        if (!string.IsNullOrWhiteSpace(data.Name) &&
                            fixedIN.TryGetValue(data.Name, out fixedNames))
                        {
                            string nsName = string.Empty;
                            switch (data.Type)
                            {
                                case ConstantType.QName:
                                {
                                    var qName = (QName)data;
                                    nsName = qName.Namespace.Name;
                                    break;
                                }
                                case ConstantType.Multiname:
                                {
                                    var mName = (Multiname)data;
                                    nsName = mName.NamespaceSet.GetNamespaces().First().Name;
                                    break;
                                }
                            }

                            string fixedName = string.Empty;
                            if (!fixedNames.TryGetValue(nsName, out fixedName))
                            {
                                continue;
                            }

                            int fixedNameIndex = 0;
                            if (!fixedINIndices.TryGetValue(fixedName, out fixedNameIndex))
                            {
                                fixedNameIndex = pool.AddString(fixedName);
                                fixedINIndices.Add(fixedName, fixedNameIndex);
                            }
                            data.NameIndex = fixedNameIndex;
                        }
                    }
                    #endregion
                }
                finally { pool.RecycleIndices = true; }
            }

            #region Symbol Fixing
            foreach (SymbolClassTag symbolTag in Tags
                .Where(t => t.Type == FlashTagType.SymbolClass)
                .Cast<SymbolClassTag>())
            {
                for (int i = 0; i < symbolTag.Names.Count; i++)
                {
                    string name = symbolTag.Names[i];

                    if (name.Contains("_-") ||
                        _reservedNames.Contains(name.Trim()))
                    {
                        string qName = name;
                        string nsName = string.Empty;

                        if (name.Contains("."))
                        {
                            string[] names = name.Split('.');
                            nsName = names[0];
                            qName = names[1];
                        }

                        name = string.Empty;
                        if (fixedNS.ContainsKey(nsName))
                        {
                            nsName = fixedNS[nsName];
                            name = (nsName + ".");
                        }

                        SortedDictionary<string, string> fixedNames = null;
                        if (fixedIN.TryGetValue(qName, out fixedNames))
                        {
                            qName = fixedNames[nsName];
                        }

                        symbolTag.Names[i] = (name + qName);
                    }
                    // TODO: What if there is a symbol named: _-000.get ?
                }
            }
            #endregion
        }
        public bool DisableHandshake()
        {
            ABCFile abc = GetABCFile(2);
            ASInstance commDemo = abc.GetClasses("HabboCommunicationDemo")[0].Instance;

            ASMethod connInit = commDemo.GetMethods(1, "void")
                .Where(m => m.Parameters[0].IsOptional &&
                            m.Parameters[0].Type.Name == "Event")
                .First();

            ASMethod pubVerifier = commDemo.GetMethods(1, "void")
                .Where(m => m.Body.MaxStack == 4 &&
                            m.Body.LocalCount == 10 &&
                            m.Body.InitialScopeDepth == 5 &&
                            m.Body.MaxScopeDepth == 6)
                .FirstOrDefault();

            if (connInit == null ||
                pubVerifier == null)
            {
                return false;
            }

            AS3Code pubVerifierCode =
                pubVerifier.Body.ParseCode();

            // Get the 13 instructions starting with: debugline 320
            IEnumerable<Instruction> postShakeInstructions = pubVerifierCode.Instructions
                .SkipWhile(i => i.OP != Operation.DebugLine ||
                                ((DebugLineIns)i).LineNumber != 320)
                .Take(13);

            AS3Code connInitCode = connInit.Body.ParseCode();
            foreach (Instruction instruction in connInitCode.Instructions)
            {
                if (JumpInstruction.IsValid(instruction.OP))
                {
                    var jumper = (JumpInstruction)instruction;

                    List<Instruction> jumpScope =
                        connInitCode.JumpScopes[jumper];

                    jumpScope.RemoveRange(
                        jumpScope.Count - 6, 6);

                    jumpScope.AddRange(postShakeInstructions);
                    break;
                }
            }
            connInit.Body.Code = connInitCode.ToArray();
            return true;
        }
        public bool ReplaceRSAKeys(string exponent, string modulus)
        {
            ABCFile abc = GetABCFile(2);
            ASClass keyObfClass = abc.GetClasses("KeyObfuscator")[0];

            int modifyCount = 0;
            foreach (ASMethod method in keyObfClass.GetMethods(0, "String"))
            {
                int keyIndex = 0;
                switch (method.TraitId)
                {
                    // Get Modulus Method
                    case 6:
                    {
                        modifyCount++;
                        keyIndex = abc.Pool.AddString(modulus);
                        break;
                    }
                    // Get Exponent Method
                    case 7:
                    {
                        modifyCount++;
                        keyIndex = abc.Pool.AddString(exponent);
                        break;
                    }

                    // Continue enumerating before reading grabbing instructions.
                    default: continue;
                }

                AS3Code methodCode = method.Body.ParseCode();
                List<Instruction> instructions = methodCode.Instructions;

                if (instructions.Count >= 2 &&
                    instructions[0].OP != Operation.PushString &&
                    instructions[1].OP != Operation.ReturnValue)
                {
                    instructions.InsertRange(0, new Instruction[]
                    {
                        new PushStringIns(abc.Pool, keyIndex),
                        new Instruction(Operation.ReturnValue)
                    });
                }

                method.Body.Code = methodCode.ToArray();
            }
            return (modifyCount == 2);
        }

        private void FindMessages()
        {
            ABCFile abc = GetABCFile(2);
            ASClass messagesClass = abc.GetClasses("HabboMessages")[0];

            ASMethod messagesCtor = messagesClass.Constructor;
            AS3Code code = messagesCtor.Body.ParseCode();

            int inMapTypeIndex = messagesClass.Traits[0].QNameIndex;
            int outMapTypeIndex = messagesClass.Traits[1].QNameIndex;

            List<Instruction> instructions = code.Instructions
                .Where(i => i.OP == Operation.GetLex ||
                            i.OP == Operation.PushShort ||
                            i.OP == Operation.PushByte).ToList();

            for (int i = 0; i < instructions.Count; i += 3)
            {
                var getLexInst = (instructions[i + 0] as GetLexIns);
                bool isOutgoing = (getLexInst.TypeNameIndex == outMapTypeIndex);

                var pushPrimitiveInst = (instructions[i + 1] as IPush);
                ushort header = Convert.ToUInt16(pushPrimitiveInst.Value);

                getLexInst = (instructions[i + 2] as GetLexIns);
                ASClass messageClass = abc.GetClasses(getLexInst.TypeName.Name)[0];

                (isOutgoing ? _outMessages : _inMessages)
                    .Add(header, messageClass);

                if (header == 4000 && isOutgoing)
                {
                    HandleRevisionMessageClass(messageClass);
                }
            }
        }
        private bool DisableHostPrepender()
        {
            ABCFile abc = GetABCFile(2);

            ASInstance commMngr = abc.GetClasses(
                "HabboCommunicationManager")[0].Instance;

            SlotConstantTrait infoHostSlot =
                commMngr.GetSlotTraits("String").First();

            int infoHostSlotNameIndex =
                abc.Pool.Multinames.IndexOf(infoHostSlot.QName);

            int getPropertyNameIndex =
                abc.Pool.GetMultinameIndices("getProperty").First();

            ASMethod initCompMethod =
                commMngr.GetMethod(0, "void", "initComponent");

            string connectMethodName = string.Empty;
            AS3Code initCompCode = initCompMethod.Body.ParseCode();

            initCompCode.Instructions.Reverse(); // Start from bottom.
            foreach (Instruction instruction in initCompCode.Instructions)
            {
                if (instruction.OP != Operation.CallPropVoid) continue;
                var callPropVoidIns = (CallPropVoidIns)instruction;

                connectMethodName = callPropVoidIns.PropertyName.Name;
                break;
            }

            ASMethod connectMethod = commMngr
                .GetMethod(0, "void", connectMethodName);

            AS3Code connectCode = connectMethod.Body.ParseCode();
            List<Instruction> instructions = connectCode.Instructions;
            instructions.InsertRange(4, new Instruction[]
            {
                new Instruction(Operation.GetLocal_0),
                new FindPropStrictIns(abc.Pool, getPropertyNameIndex),
                new PushStringIns(abc.Pool, "connection.info.host"),
                new CallPropertyIns(abc.Pool, getPropertyNameIndex, 1),
                new InitPropertyIns(abc.Pool, infoHostSlotNameIndex)
            });

            int magicInverseIndex = abc.Pool.AddInteger(65290);
            foreach (Instruction instruction in instructions)
            {
                if (instruction.OP != Operation.PushInt) continue;
                var pushIntIns = (PushIntIns)instruction;

                pushIntIns.ValueIndex = magicInverseIndex;
            }

            connectMethod.Body.Code = connectCode.ToArray();
            return true;
        }
        private void FindValidHostsRegexChecker()
        {
            ASClass habboClass = GetABCFile(1).GetClasses("Habbo")[0];
            ASInstance habboInstance = habboClass.Instance;

            _validHostsRegexChecker = habboInstance.GetMethods(2, "Boolean")
                .Where(m => m.Parameters[0].Type.Name == "String" &&
                            m.Parameters[1].Type.Name == "Object").First();

            AS3Code validHostCode =
                _validHostsRegexChecker.Body.ParseCode();

            int index = 0;
            foreach (Instruction instruction in validHostCode.Instructions)
            {
                // All of the regex patterns were retrieved, I think.
                if (index == MAX_REGEX_PATTERNS) break;

                if (instruction.OP != Operation.PushString) continue;
                var pushStringInst = (instruction as PushStringIns);

                ValidHostsRegexPatterns[index] = pushStringInst.Value;
                _validHostsRegexPatternsIndices[index++] = pushStringInst.ValueIndex;
            }
        }
        private void HandleRevisionMessageClass(ASClass messageClass)
        {
            ASInstance messageInstance = messageClass.Instance;
            ASMethod toArrayMethod = messageInstance.GetMethods(0, "Array").First();

            AS3Code toArrayCode = toArrayMethod.Body.ParseCode();
            foreach (Instruction instruction in toArrayCode.Instructions)
            {
                if (instruction.OP != Operation.PushString) continue;
                var pushStringInst = (instruction as PushStringIns);

                _revisionIndex = pushStringInst.ValueIndex;
                break;
            }
        }

        protected ABCFile GetABCFile(int index)
        {
            ABCFile abc = null;
            DoABCTag abcTag = _abcTags[index];
            if (!_abcFiles.TryGetValue(abcTag, out abc))
            {
                abc = new ABCFile(abcTag.ABCData);
                _abcFiles[abcTag] = abc;
            }
            return abc;
        }
        protected override FlashTag ReadTag(HeaderRecord header)
        {
            FlashTag tag = base.ReadTag(header);
            if (tag.Type == FlashTagType.DoABC)
            {
                var abcTag = (DoABCTag)tag;
                _abcTags.Add(abcTag);

                var abc = new ABCFile(abcTag.ABCData);
                _abcFiles[abcTag] = abc;
            }
            return tag;
        }
        protected override void WriteTag(FlashTag tag, FlashWriter output)
        {
            var doABCTag = (tag as DoABCTag);
            if (doABCTag != null)
            {
                ABCFile abcFile = null;
                if (_abcFiles.TryGetValue(doABCTag, out abcFile))
                {
                    doABCTag.ABCData = abcFile.ToArray();
                }
            }
            base.WriteTag(tag, output);
        }

        public override void Assemble()
        {
            ABCFile abc = GetABCFile(1);
            for (int i = 0; i < ValidHostsRegexPatterns.Length; i++)
            {
                string pattern = ValidHostsRegexPatterns[i];
                if (string.IsNullOrWhiteSpace(pattern)) continue;

                // Won't hurt to set it even though it hasn't changed, it's fine.
                abc.Pool.SetString(_validHostsRegexPatternsIndices[i], pattern);
            }
            base.Assemble();
        }
        public override void Disassemble()
        {
            base.Disassemble();
            FindMessages();

            FindValidHostsRegexChecker();
        }
    }
}