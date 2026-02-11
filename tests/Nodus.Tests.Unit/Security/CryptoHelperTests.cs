using System.Security.Cryptography;
using Nodus.Shared.Security;
using Xunit;

namespace Nodus.Tests.Unit.Security;

public class CryptoHelperTests
{
    private byte[] GenerateRandomKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    [Fact]
    public void GenerateSigningKeys_ReturnsValidBase64Keys()
    {
        // Act
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();

        // Assert
        Assert.NotNull(publicKey);
        Assert.NotNull(privateKey);
        Assert.NotEmpty(publicKey);
        Assert.NotEmpty(privateKey);
        
        var pubBytes = Convert.FromBase64String(publicKey);
        var privBytes = Convert.FromBase64String(privateKey);
        
        // ECDsa P-256 keys are usually around ~91 bytes (DER encoded) but variable
        Assert.True(pubBytes.Length > 0);
        Assert.True(privBytes.Length > 0);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_Success()
    {
        // Arrange
        var aesKey = GenerateRandomKey();
        var plaintext = "Hello, Nodus!";
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        // Act
        var ciphertext = CryptoHelper.Encrypt(plaintextBytes, aesKey);
        var decryptedBytes = CryptoHelper.Decrypt(ciphertext, aesKey);

        // Assert
        Assert.NotNull(ciphertext);
        Assert.NotNull(decryptedBytes);
        
        var decryptedText = System.Text.Encoding.UTF8.GetString(decryptedBytes);
        Assert.Equal(plaintext, decryptedText);
    }

    [Fact]
    public void Encrypt_DifferentNonces_ProducesDifferentCiphertext()
    {
        // Arrange
        var aesKey = GenerateRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");

        // Act
        var ciphertext1 = CryptoHelper.Encrypt(plaintext, aesKey);
        var ciphertext2 = CryptoHelper.Encrypt(plaintext, aesKey);

        // Assert
        Assert.NotEqual(ciphertext1, ciphertext2); // Different nonces
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsException()
    {
        // Arrange
        var aesKey1 = GenerateRandomKey();
        var aesKey2 = GenerateRandomKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var ciphertext = CryptoHelper.Encrypt(plaintext, aesKey1);

        // Act & Assert
        Assert.ThrowsAny<CryptographicException>(() => CryptoHelper.Decrypt(ciphertext, aesKey2));
    }

    [Fact]
    public void SignVerify_RoundTrip_Success()
    {
        // Arrange
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();
        var message = System.Text.Encoding.UTF8.GetBytes("Sign this message");

        // Act
        var signature = CryptoHelper.SignData(message, privateKey);
        var isValid = CryptoHelper.VerifyData(message, signature, publicKey);

        // Assert
        Assert.NotNull(signature);
        Assert.True(signature.Length > 0);
        Assert.True(isValid);
    }

    [Fact]  
    public void VerifyData_WrongSignature_Fails()
    {
        // Arrange
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();
        var message = System.Text.Encoding.UTF8.GetBytes("Original message");
        var signature = CryptoHelper.SignData(message, privateKey);

        // Tamper with signature
        if (signature.Length > 0)
            signature[0] ^= 0xFF;

        // Act
        var isValid = CryptoHelper.VerifyData(message, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyData_WrongPublicKey_Fails()
    {
        // Arrange
        var (publicKey1, privateKey1) = CryptoHelper.GenerateSigningKeys();
        var (publicKey2, _) = CryptoHelper.GenerateSigningKeys();
        var message = System.Text.Encoding.UTF8.GetBytes("Message");
        var signature = CryptoHelper.SignData(message, privateKey1);

        // Act
        var isValid = CryptoHelper.VerifyData(message, signature, publicKey2);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyData_TamperedMessage_Fails()
    {
        // Arrange
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();
        var message = System.Text.Encoding.UTF8.GetBytes("Original message");
        var signature = CryptoHelper.SignData(message, privateKey);

        // Tamper with message
        var tamperedMessage = System.Text.Encoding.UTF8.GetBytes("Tampered message");

        // Act
        var isValid = CryptoHelper.VerifyData(tamperedMessage, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }
}
