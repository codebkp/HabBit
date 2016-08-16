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

namespace HabBit.Habbo
{
    public class HGame : ShockwaveFlash
    {
        private ASMethod _validHostsRegexChecker;

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
        public bool BypassDomainChecks(bool avoidValidHostsRegexChecks)
        {
            var domainChecks = new List<ASMethod>(2);

            ABCFile abc = GetABCFile(0);
            if (abc == null) return false;

            ASMethod isLocalHostCheck = GetABCFile(0)?.Classes?[0]
                .GetMethods(1, "Boolean").FirstOrDefault();

            if (isLocalHostCheck != null)
                domainChecks.Add(isLocalHostCheck);

            if (!avoidValidHostsRegexChecks &&
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

        public bool ReplaceRSAKeys(string exponent, string modulus)
        {
            ABCFile abc = GetABCFile(2);
            ASClass keyObfuscClass = abc.GetClasses("KeyObfuscator")[0];

            var newInstructions = new List<Instruction>();
            var pushKeyInst = new PushStringIns(abc.Pool);

            newInstructions.Add(pushKeyInst);
            newInstructions.Add(new Instruction(Operation.ReturnValue));

            int modCount = 0;
            foreach (ASMethod method in keyObfuscClass.GetMethods(0, "String"))
            {
                List<Instruction> instructions =
                    method.Body.GetInstructions().ToList();

                switch (method.TraitId)
                {
                    case 6: // Return Modulus Method
                    {
                        modCount++;
                        pushKeyInst.ValueIndex = abc.Pool.AddString(modulus);
                        break;
                    }
                    case 7: // Return Exponent Method
                    {
                        modCount++;
                        pushKeyInst.ValueIndex = abc.Pool.AddString(exponent);
                        break;
                    }
                }

                newInstructions.AddRange(method.Body.GetInstructions());
                method.Body.SetInstructions(newInstructions);
            }
            return (modCount == 2);
        }

        private void FindMessages()
        {
            ABCFile abc = GetABCFile(2);
            ASClass messagesClass = abc.GetClasses("HabboMessages")[0];

            ASMethod messagesCtor = messagesClass.Constructor;
            int inMapTypeIndex = messagesClass.Traits[0].QNameIndex;
            int outMapTypeIndex = messagesClass.Traits[1].QNameIndex;

            List<Instruction> instructions = messagesCtor.Body.GetInstructions()
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
            foreach (Instruction instruction in initCompMethod.Body
                .GetInstructions().Reverse())
            {
                if (instruction.OP == Operation.CallPropVoid)
                {
                    connectMethodName =
                        ((CallPropVoidIns)instruction).PropertyName.Name;

                    break;
                }
            }

            ASMethod connectMethod = commMngr
                .GetMethod(0, "void", connectMethodName);

            var instructions = new List<Instruction>(
                connectMethod.Body.GetInstructions());

            var getPropSet = new List<Instruction>();
            getPropSet.Add(new Instruction(Operation.GetLocal_0));
            getPropSet.Add(new FindPropStrictIns(abc.Pool, getPropertyNameIndex));
            getPropSet.Add(new PushStringIns(abc.Pool, "connection.info.host"));
            getPropSet.Add(new CallPropertyIns(abc.Pool, getPropertyNameIndex, 1));
            getPropSet.Add(new InitPropertyIns(abc.Pool, infoHostSlotNameIndex));
            instructions.InsertRange(0, getPropSet);

            int magicInverseIndex = abc.Pool.AddInteger(65290);
            foreach (Instruction instruction in instructions)
            {
                if (instruction.OP != Operation.PushInt) continue;

                var pushIntIns = (PushIntIns)instruction;
                pushIntIns.ValueIndex = magicInverseIndex;
            }

            connectMethod.Body.SetInstructions(instructions);
            return true;
        }
        private void FindValidHostsRegexChecker()
        {
            ASClass habboClass = GetABCFile(1).GetClasses("Habbo")[0];
            ASInstance habboInstance = habboClass.Instance;

            _validHostsRegexChecker = habboInstance.GetMethods(2, "Boolean")
                .Where(m => m.Parameters[0].Type.Name == "String" &&
                            m.Parameters[1].Type.Name == "Object").First();

            int index = 0;
            foreach (Instruction instruction in
                _validHostsRegexChecker.Body.GetInstructions())
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

            foreach (Instruction instruction in
                toArrayMethod.Body.GetInstructions())
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