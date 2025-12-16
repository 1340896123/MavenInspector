using System.Text;
using System.Buffers.Binary;

namespace MavenInspector;

/// <summary>
/// A lightweight parser for Java Class Files to extract Class Name and Method Names.
/// </summary>
public class JavaClassParser
{
    private readonly byte[] _data;
    private int _offset;
    private ConstantPoolEntry[] _constantPool;

    public string ClassName { get; private set; }
    public List<string> MethodNames { get; private set; } = new();

    public JavaClassParser(byte[] data)
    {
        _data = data;
    }

    public void Parse()
    {
        _offset = 0;
        if (_data.Length < 10) return;

        // Magic Number: 0xCAFEBABE
        if (ReadU4() != 0xCAFEBABE) return;

        ReadU2(); // Minor Version
        ReadU2(); // Major Version

        // Constant Pool
        int cpCount = ReadU2();
        _constantPool = new ConstantPoolEntry[cpCount]; // Index 1 to cpCount-1

        for (int i = 1; i < cpCount; i++)
        {
            byte tag = ReadU1();
            switch (tag)
            {
                case 1: // Utf8
                    int len = ReadU2();
                    string utf8 = Encoding.UTF8.GetString(_data, _offset, len);
                    _constantPool[i] = new ConstantPoolEntry { Tag = tag, StringValue = utf8 };
                    _offset += len;
                    break;
                case 7: // Class
                    int nameIndex = ReadU2();
                    _constantPool[i] = new ConstantPoolEntry { Tag = tag, RefIndex1 = nameIndex };
                    break;
                case 3: // Integer
                case 4: // Float
                    _offset += 4;
                    break;
                case 5: // Long
                case 6: // Double
                    _offset += 8;
                    i++; // Takes two slots
                    break;
                case 8: // String
                    ReadU2(); 
                    break;
                case 9: // Fieldref
                case 10: // Methodref
                case 11: // InterfaceMethodref
                case 12: // NameAndType
                    _offset += 4;
                    break;
                case 15: // MethodHandle
                    _offset += 3;
                    break;
                case 16: // MethodType
                    _offset += 2;
                    break;
                case 18: // InvokeDynamic
                    _offset += 4;
                    break; 
                 // Java 9+ Modules etc might have more, simplified for now
                default: 
                    // If we hit an unknown tag, we might fail to parse the rest correctly 
                    // because we don't know the size. But for basic method extraction usually fine.
                    // However, practically, if we fail here, we stop.
                    return;
            }
        }

        ReadU2(); // Access Flags
        int thisClassIndex = ReadU2();
        ClassName = ResolveClass(thisClassIndex).Replace('/', '.');
        
        ReadU2(); // Super Class
        
        int interfaceCount = ReadU2();
        _offset += interfaceCount * 2;

        int fieldCount = ReadU2();
        for (int i = 0; i < fieldCount; i++)
        {
            ReadU2(); // Access Flags
            ReadU2(); // Name Index
            ReadU2(); // Descriptor Index
            int attrCount = ReadU2();
            for (int j = 0; j < attrCount; j++)
            {
                ReadU2(); // Attr Name Index
                int attrLen = (int)ReadU4();
                _offset += attrLen;
            }
        }

        int methodCount = ReadU2();
        for (int i = 0; i < methodCount; i++)
        {
            ReadU2(); // Access Flags
            int nameIndex = ReadU2();
            int descIndex = ReadU2();
            
            string methodName = ResolveUtf8(nameIndex);
            if (!string.IsNullOrEmpty(methodName) && methodName != "<init>" && methodName != "<clinit>")
            {
                MethodNames.Add(methodName);
            }

            int attrCount = ReadU2();
            for (int j = 0; j < attrCount; j++)
            {
                ReadU2(); // Attr Name Index
                int attrLen = (int)ReadU4();
                _offset += attrLen;
            }
        }
    }

    private string ResolveUtf8(int index)
    {
        if (index > 0 && index < _constantPool.Length && _constantPool[index]?.Tag == 1)
        {
            return _constantPool[index].StringValue;
        }
        return null;
    }

    private string ResolveClass(int index)
    {
        if (index > 0 && index < _constantPool.Length && _constantPool[index]?.Tag == 7)
        {
            return ResolveUtf8(_constantPool[index].RefIndex1);
        }
        return "";
    }

    private byte ReadU1() => _data[_offset++];
    private ushort ReadU2()
    {
        var val = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_offset));
        _offset += 2;
        return val;
    }
    private uint ReadU4()
    {
         var val = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(_offset));
         _offset += 4;
         return val;
    }

    private class ConstantPoolEntry
    {
        public byte Tag;
        public string StringValue;
        public int RefIndex1;
    }
}
