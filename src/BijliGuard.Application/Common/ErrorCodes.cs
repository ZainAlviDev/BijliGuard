namespace BijliGuard.Application.Common;

/// <summary>Stable error codes. The web and mobile apps map these to user-facing messages.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string EmailAlreadyRegistered = "EMAIL_ALREADY_REGISTERED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailNotVerified = "EMAIL_NOT_VERIFIED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string OtpInvalid = "OTP_INVALID";
    public const string OtpExpired = "OTP_EXPIRED";
    public const string OtpNotFound = "OTP_NOT_FOUND";
    public const string OtpTooManyAttempts = "OTP_TOO_MANY_ATTEMPTS";
    public const string OtpResendCooldown = "OTP_RESEND_COOLDOWN";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string TokenReused = "TOKEN_REUSED";
    public const string EmailDeliveryFailed = "EMAIL_DELIVERY_FAILED";

    // Household, bills and readings
    public const string HouseholdRequired = "HOUSEHOLD_REQUIRED";
    public const string BaselineRequired = "BASELINE_REQUIRED";
    public const string DuplicateReading = "DUPLICATE_READING";
    public const string CycleEnded = "CYCLE_ENDED";
    public const string NotFound = "NOT_FOUND";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string UnsupportedFile = "UNSUPPORTED_FILE";
    public const string EmptyFile = "EMPTY_FILE";
    public const string RolloverPending = "ROLLOVER_PENDING";
    public const string RolloverNotPending = "ROLLOVER_NOT_PENDING";
    public const string ReadingNotCorrectable = "READING_NOT_CORRECTABLE";
    public const string PhotoAlreadyUsed = "PHOTO_ALREADY_USED";
    public const string ConsentVersionMismatch = "CONSENT_VERSION_MISMATCH";
    public const string OcrUnavailable = "OCR_UNAVAILABLE";
    public const string PdfNotSupported = "PDF_NOT_SUPPORTED";
    public const string PdfNoText = "PDF_NO_TEXT";
}
