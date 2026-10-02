namespace MultiTravel.Gameplay.UI
{
    /// <summary>Turkish strings of the VR panel (configurable texts such as instructions come from RuntimeConfig).</summary>
    public static class VrUiTexts
    {
        public const string WelcomeHint = "Görevli oyunu birazdan başlatacak.";

        public const string RegistrationHeading = "Kayıt";
        public const string RegistrationBody = "Görevli kayıt bilgilerinizi giriyor.\nLütfen bekleyin.";

        public const string GenderHeading = "Ürün Seti";
        public const string GenderBody = "Görevli size uygun ürün setini seçiyor.";

        public const string InstructionsHeading = "Nasıl Oynanır?";

        public const string LoadingHeading = "Hazırlanıyor...";
        public const string LoadingBody = "Ürünler odaya yerleştiriliyor.";

        public const string CountdownHeading = "Hazır ol!";
        public const string CountdownGo = "Başla!";

        public const string PlayingHeading = "Valizini hazırla!";
        public const string PlayingBodyRequired = "Gerekli tüm ürünleri valize yerleştir.";
        public const string PlayingBodyManual = "Hazır olduğunda \"Valizi Tamamla\" düğmesine bas.";
        public const string PlayingBodyEither = "Gerekli ürünleri valize yerleştir ya da \"Valizi Tamamla\" düğmesine bas.";

        public const string CompletedHeading = "Tebrikler!";
        public const string CompletedStatus = "Sonucun kaydediliyor...";
        public const string SubmittingStatus = "Gönderiliyor...";
        public const string SubmissionFailedStatus = "Sonuç gönderilemedi. Görevli tekrar deneyecek.";

        public const string FinishedHeading = "Teşekkürler!";
        public const string FinishedStatus = "Sonucun liderlik tablosuna kaydedildi.";
        public const string AbandonedHeading = "Oturum sonlandırıldı";
        public const string AbandonedStatus = "Görevli yeni oyunu hazırlayacak.";

        public const string FatalHeading = "Bir sorun oluştu";
        public const string FatalHint = "Lütfen görevliye haber verin.";

        public const string ScoreFormat = "Puan: {0:0}";
        public const string TimerPrefix = "Süre: ";
        public const string ProgressFormat = "{0:0}/{1:0} gerekli ürün";

        public const string SummaryScoreLabel = "Puan: ";
        public const string SummaryTimeLabel = "Süre: ";
        public const string SummaryCorrectLabel = "Doğru ürün: ";
        public const string SummaryIncorrectLabel = "Yanlış ürün: ";
        public const string SummaryRankLabel = "Sıralaman: ";
    }
}
