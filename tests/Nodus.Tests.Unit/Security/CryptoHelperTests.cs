using System.Security.Cryptography;
using System.Text;
using Xunit;
using Nodus.Shared.Security;

namespace Nodus.Tests.Security;

public class CryptoHelperTests
{
    [Fact]
    public void DeriveKeyFromPassword_ValidInputs_Returns32ByteKey()
    {
        // Arrange
        var password = "TestPassword123!";
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        // Act
        var key = CryptoHelper.DeriveKeyFromPassword(password, salt);

        // Assert
        Assert.NotNull(key);
        Assert.Equal(32, key.Length);
    }

    [Fact]
    public void DeriveKeyFromPassword_SameInputs_ReturnsSameKey()
    {
        // Arrange
        var password = "TestPassword123!";
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        // Act
        var key1 = CryptoHelper.DeriveKeyFromPassword(password, salt);
        var key2 = CryptoHelper.DeriveKeyFromPassword(password, salt);

        // Assert
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKeyFromPassword_DifferentPasswords_ReturnsDifferentKeys()
    {
        // Arrange
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        // Act
        var key1 = CryptoHelper.DeriveKeyFromPassword("Password1", salt);
        var key2 = CryptoHelper.DeriveKeyFromPassword("Password2", salt);

        // Assert
        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKeyFromPassword_InvalidSalt_ThrowsArgumentException()
    {
        // Arrange
        var password = "TestPassword";
        var invalidSalt = new byte[8]; // Wrong size

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            CryptoHelper.DeriveKeyFromPassword(password, invalidSalt));
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_Success()
    {
        // Arrange
        var plaintext = "Hello, Nodus!"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);

        // Act
        var encrypted = CryptoHelper.Encrypt(plaintext, key);
        var decrypted = CryptoHelper.Decrypt(encrypted, key);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesNonceAndTag()
    {
        // Arrange
        var plaintext = "Test"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);

        // Act
        var encrypted = CryptoHelper.Encrypt(plaintext, key);

        // Assert
        // Encrypted should be: 12 bytes (nonce) + plaintext length + 16 bytes (tag)
        Assert.Equal(12 + plaintext.Length + 16, encrypted.Length);
    }

    [Fact]
    public void Encrypt_DifferentNonces_ProducesDifferentCiphertexts()
    {
        // Arrange
        var plaintext = "Same message"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);

        // Act
        var encrypted1 = CryptoHelper.Encrypt(plaintext, key);
        var encrypted2 = CryptoHelper.Encrypt(plaintext, key);

        // Assert
        Assert.NotEqual(encrypted1, encrypted2); // Different nonces
    }

    [Fact]
    public void Decrypt_TamperedData_ThrowsCryptographicException()
    {
        // Arrange
        var plaintext = "Secret message"u8.ToArray();
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var encrypted = CryptoHelper.Encrypt(plaintext, key);

        // Tamper with the ciphertext
        encrypted[20] ^= 0xFF;

        // Act & Assert
        Assert.Throws<AuthenticationTagMismatchException>(() => 
            CryptoHelper.Decrypt(encrypted, key));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        // Arrange
        var plaintext = "Secret"u8.ToArray();
        var key1 = new byte[32];
        var key2 = new byte[32];
        RandomNumberGenerator.Fill(key1);
        RandomNumberGenerator.Fill(key2);

        var encrypted = CryptoHelper.Encrypt(plaintext, key1);

        // Act & Assert
        Assert.Throws<AuthenticationTagMismatchException>(() => 
            CryptoHelper.Decrypt(encrypted, key2));
    }

    [Fact]
    public void EncryptString_DecryptString_RoundTrip_Success()
    {
        // Arrange
        var plaintext = "Hello, World! 🌍";
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);

        // Act
        var encrypted = CryptoHelper.EncryptString(plaintext, key);
        var decrypted = CryptoHelper.DecryptString(encrypted, key);

        // Assert
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void GenerateSigningKeys_ReturnsValidKeyPair()
    {
        // Act
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();

        // Assert
        Assert.NotNull(publicKey);
        Assert.NotNull(privateKey);
        Assert.NotEmpty(publicKey);
        Assert.NotEmpty(privateKey);
        Assert.NotEqual(publicKey, privateKey);
    }

    [Fact]
    public void SignData_VerifyData_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var data = "Important message"u8.ToArray();
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();

        // Act
        var signature = CryptoHelper.SignData(data, privateKey);
        var isValid = CryptoHelper.VerifyData(data, signature, publicKey);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyData_TamperedData_ReturnsFalse()
    {
        // Arrange
        var data = "Original message"u8.ToArray();
        var tamperedData = "Tampered message"u8.ToArray();
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();
        var signature = CryptoHelper.SignData(data, privateKey);

        // Act
        var isValid = CryptoHelper.VerifyData(tamperedData, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyData_TamperedSignature_ReturnsFalse()
    {
        // Arrange
        var data = "Message"u8.ToArray();
        var (publicKey, privateKey) = CryptoHelper.GenerateSigningKeys();
        var signature = CryptoHelper.SignData(data, privateKey);
        
        // Tamper with signature
        signature[0] ^= 0xFF;

        // Act
        var isValid = CryptoHelper.VerifyData(data, signature, publicKey);

        // Assert
        Assert.False(isValid);
    }
}
