namespace CvTailor.Api.Models
{
    // Kasadaki liste kayıtları (deneyim, eğitim, proje, madde, beceri, sertifika). VaultService kaydederken
    // gelen listeyi mevcut kayıtlarla Id üzerinden eşleştiriyor.
    public interface IVaultItem
    {
        Guid Id { get; set; }
    }
}
