using static GrpLocalizationEditor.Ui;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace GrpLocalizationEditor;

internal static class OasisCipher
{
    private static readonly Lazy<byte[]> Key = new(() => EmbeddedOasis.Read("Oasis.Key"));

    public static byte[] Convert(byte[] input, bool decrypt)
    {
        var key = Key.Value;
        if (key.Length != 32)
            throw new InvalidDataException(T("TheEmbeddedOasisKeyIsInvalid"));
        if (!decrypt)
            _ = OasisDocument.Parse(input);

        var cipher = new TwofishEngine();
        cipher.Init(true, new KeyParameter(key));
        // Oasis uses Twofish CFB1 with the first 16 ASCII key bytes as its IV.
        var feedback = key.AsSpan(0, 16).ToArray();
        var block = new byte[16];
        var output = new byte[input.Length];
        for (int index = 0; index < input.Length; index++)
        {
            for (int shift = 7; shift >= 0; shift--)
            {
                cipher.ProcessBlock(feedback, 0, block, 0);
                int inputBit = (input[index] >> shift) & 1;
                int outputBit = inputBit ^ (block[0] >> 7);
                output[index] |= (byte)(outputBit << shift);

                for (int i = 0; i < feedback.Length - 1; i++)
                    feedback[i] = (byte)((feedback[i] << 1) | (feedback[i + 1] >> 7));
                feedback[^1] = (byte)((feedback[^1] << 1) | (decrypt ? inputBit : outputBit));
            }
        }

        if (decrypt)
        {
            try
            {
                _ = OasisDocument.Parse(output);
            }
            catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
            {
                throw new InvalidDataException(T("TheFileCouldNotBeOpened"), ex);
            }
        }
        return output;
    }
}
