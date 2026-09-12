using System.IO;
using System.Security.Cryptography;

namespace OptimaBrowser;

/// <summary>
/// Optima Vault — власне шифрування даних браузера.
/// AES-256-GCM (AEAD), ключ виводиться з майстер-пароля через PBKDF2 (SHA-256, 100 000 ітерацій).
/// Формат файлу: magic "OPT1" + salt(16) + nonce(12) + ciphertext + tag(16).
/// ponytail: шифрується лише сховище (історія/закладки). Трафік TLS не чіпаємо —
/// поверх TLS додатковий шар неможливий технічно; про це чесно написано в README.
/// </summary>
public static class Vault
{
    private static readonly byte[] Magic = { 0x4F, 0x50, 0x54, 0x31 }; // "OPT1"

    public static byte[] Encrypt(byte[] plain, string pass)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = Derive(pass, salt);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] ct = new byte[plain.Length];
        byte[] tag = new byte[16];
        using (var aes = new AesGcm(key, 16))
            aes.Encrypt(nonce, plain, ct, tag);

        using var ms = new MemoryStream();
        ms.Write(Magic, 0, 4);
        ms.Write(salt, 0, 16);
        ms.Write(nonce, 0, 12);
        ms.Write(ct, 0, ct.Length);
        ms.Write(tag, 0, 16);
        return ms.ToArray();
    }

    public static byte[] Decrypt(byte[] blob, string pass)
    {
        if (blob.Length < 4 + 16 + 12 + 16 || !blob.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Не Optima Vault файл");

        int p = 4;
        byte[] salt = blob[p..(p + 16)]; p += 16;
        byte[] nonce = blob[p..(p + 12)]; p += 12;
        int ctLen = blob.Length - p - 16;
        byte[] ct = blob[p..(p + ctLen)]; p += ctLen;
        byte[] tag = blob[p..(p + 16)];

        byte[] key = Derive(pass, salt);
        byte[] plain = new byte[ctLen];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, ct, tag, plain); // CryptographicException при невірному паролі
        return plain;
    }

    private static byte[] Derive(string pass, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pass, salt, 100_000, HashAlgorithmName.SHA256, 32);
}