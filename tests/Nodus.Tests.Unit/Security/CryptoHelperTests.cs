using Nodus.Shared.Security;
using Xunit;

namespace Nodus.Tests.Unit.Security;

public class CryptoHelperTests
{
    [Fact]
    public void GenerateAesKey_ReturnsValidBase64Key()
    {
        // Act
        var key = CryptoHelper.GenerateAesKey();

        // Assert
        Assert.NotNull(key);
        Assert.NotEmpty(key);
        var bytes = Convert.FromBase64String(key);
        Assert.Equal(32, bytes.Length); // 256 bits
    }

    [Fact]
    public void GenerateEd25519KeyPair_ReturnsValidKeys()
    {
        // Act
        var (privateKey, publicKey) = CryptoHelper.GenerateEd25519KeyPair();

        // Assert
        Assert.NotNull(privateKey);
        Assert.NotNull(publicKey);
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);
        
        var privBytes = Convert.FromBase64String(privateKey);
        var pubBytes = Convert.FromBase64String(publicKey);
        
        Assert.Equal(32, privBytes.Length);
        Assert.Equal(32, pubBytes.Length);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrip_Success()
    {
        // Arrange
        var aesKey = CryptoHelper.GenerateAesKey();
        var plaintext = "Hello, Nodus!";
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        // Act
        var ciphertext = CryptoHelper.EncryptAesGcm(plaintextBytes, aesKey);
        var decrypted = CryptoHelper.DecryptAesGcm(ciphertext, aesKey);

        // Assert
        Assert.NotNull(ciphertext);
        Assert.NotNull(decrypted);
        Assert.True(decrypted.IsSuccess);
        
        var decryptedText = System.Text.Encoding.UTF8.GetString(decrypted.Value);
        Assert.Equal(plaintext, decryptedText);
    }

    [Fact]
    public void EncryptAesGcm_DifferentNonces_ProducesDifferentCiphertext()
    {
        // Arrange
        var aesKey = CryptoHelper.GenerateAesKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Test message");

        // Act
        var ciphertext1 = CryptoHelper.EncryptAesGcm(plaintext, aesKey);
        var ciphertext2 = CryptoHelper.EncryptAesGcm(plaintext, aesKey);

        // Assert
        Assert.NotEqual(ciphertext1, ciphertext2); // Different nonces
    }

    [Fact]
    public void DecryptAesGcm_WrongKey_Fails()
    {
        // Arrange
        var aesKey1 = CryptoHelper.GenerateAesKey();
        var aesKey2 = CryptoHelper.GenerateAesKey();
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Secret");
        var ciphertext = CryptoHelper.EncryptAesGcm(plaintext, aesKey1);

        // Act
        var result = CryptoHelper.DecryptAesGcm(ciphertext, aesKey2);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("decrypt", result.Error.ToLower());
    }

    [Fact]
    public void SignVerify_RoundTrip_Success()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoHelper.GenerateEd25519KeyPair();
        var message = System.Text.Encoding.UTF8.GetBytes("Sign this message");

        // Act
        var signature = CryptoHelper.SignEd25519(message, privateKey);
        var isValid = CryptoHelper.VerifyEd25519(message, signature, publicKey);

        // Assert
        Assert.NotNull(signature);
        Assert.Equal(64, signature.Length); // Ed25519 signature size
        Assert.True(isValid);
    }

    [Fact]  
    public void VerifyEd25519_WrongSignature_Fails()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoHelper.GenerateEd25519KeyPair();
        var message = System.Text.Encoding.UTF8.GetBytes("Original message");
        var signature = CryptoHelper.SignEd25519(message, privateKey);

        // Tamper with signature
        signature[0] ^= 0xFF;

        // Act
        var isValid = CryptoHelper.VerifyEd25519(message, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyEd25519_WrongPublicKey_Fails()
    {
        // Arrange
        var (privateKey1, publicKey1) = CryptoHelper.GenerateEd25519KeyPair();
        var (_, publicKey2) = CryptoHelper.GenerateEd25519KeyPair();
        var message = System.Text.Encoding.UTF8.GetBytes("Message");
        var signature = CryptoHelper.SignEd25519(message, privateKey1);

        // Act
        var isValid = CryptoHelper.VerifyEd25519(message, signature, publicKey2);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyEd25519_TamperedMessage_Fails()
    {
        // Arrange
        var (privateKey, publicKey) = CryptoHelper.GenerateEd25519KeyPair();
        var message = System.Text.Encoding.UTF8.GetBytes("Original message");
        var signature = CryptoHelper.SignEd25519(message, privateKey);

        // Tamper with message
        var tamperedMessage = System.Text.Encoding.UTF8.GetBytes("Tampered message");

        // Act
        var isValid = CryptoHelper.VerifyEd25519(tamperedMessage, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }
}
