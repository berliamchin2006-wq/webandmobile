using System;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HomeNest.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        private (SmtpClient client, string senderEmail, string senderName) GetSmtpClient()
        {
            string host = _config["Smtp:Host"] ?? "smtp.gmail.com";
            int port = int.TryParse(_config["Smtp:Port"], out int p) ? p : 587;
            bool enableSsl = bool.TryParse(_config["Smtp:EnableSsl"], out bool ssl) ? ssl : true;
            string senderEmail = _config["Smtp:SenderEmail"] ?? "AirbnbBookingSystem@gmail.com";
            string senderName = _config["Smtp:SenderName"] ?? "Airbnb Booking System";
            string rawPassword = _config["Smtp:AppPassword"] ?? "efni eeds dvqc eexz";
            string appPassword = rawPassword.Replace(" ", "").Trim();

            var client = new SmtpClient(host)
            {
                Port = port,
                Credentials = new NetworkCredential(senderEmail, appPassword),
                EnableSsl = enableSsl,
                Timeout = 10000 // 10s timeout to prevent web requests from hanging
            };

            return (client, senderEmail, senderName);
        }

        // ==============================================================
        // 1. Account Activation / OTP Email (Slide 22, 25, 31 equivalent)
        // ==============================================================
        public async Task<bool> SendVerificationCodeAsync(string recipientEmail, string otpCode, string? recipientName = null, string? verifyUrl = null)
        {
            try
            {
                var (smtpClient, senderEmail, senderName) = GetSmtpClient();

                string displayName = string.IsNullOrWhiteSpace(recipientName) ? "Valued Guest / Host" : recipientName;
                string linkSection = !string.IsNullOrEmpty(verifyUrl)
                    ? $@"<p style='margin-top:20px;'><a href='{verifyUrl}' style='display:inline-block;padding:12px 24px;background:#ff385c;color:#ffffff;text-decoration:none;border-radius:8px;font-weight:bold;'>Click Here to Verify Account Instantly</a></p>
                        <p style='color:#64748b;font-size:12px;'>Or copy and paste this link into your browser:<br/><span style='word-break:break-all;'>{verifyUrl}</span></p>"
                    : "";

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = $"[{otpCode}] Activate Your Airbnb Booking System Account",
                    Body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; background: #ffffff;'>
                            <div style='background: #ff385c; padding: 24px; text-align: center;'>
                                <h1 style='color: #ffffff; margin: 0; font-size: 24px; font-weight: 800;'>HomeNest Airbnb</h1>
                            </div>
                            <div style='padding: 30px; color: #334155;'>
                                <h2 style='color: #0f172a; margin-top: 0;'>Account Verification</h2>
                                <p>Hello <b>{displayName}</b>,</p>
                                <p>Thank you for registering on <b>Airbnb Booking System</b>. To complete your registration and activate your account, please enter the following 6-digit verification code:</p>
                                <div style='text-align: center; margin: 25px 0;'>
                                    <div style='display: inline-block; background: #f8fafc; border: 2px dashed #ff385c; padding: 14px 28px; border-radius: 10px;'>
                                        <span style='font-size: 32px; font-weight: 800; letter-spacing: 6px; color: #ff385c;'>{otpCode}</span>
                                    </div>
                                    <p style='color: #94a3b8; font-size: 13px; margin-top: 8px;'>This verification code will expire in 15 minutes.</p>
                                </div>
                                {linkSection}
                                <hr style='border: none; border-top: 1px solid #f1f5f9; margin: 25px 0;' />
                                <p style='color: #94a3b8; font-size: 12px;'>If you did not request this verification email, please safely ignore it.</p>
                            </div>
                        </div>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(recipientEmail);
                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Verification email sent to {Email} with OTP {Otp}", recipientEmail, otpCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver verification email to {Email}. Network or SMTP authentication restriction encountered.", recipientEmail);
                return false;
            }
        }

        // ==============================================================
        // 2. Self-Service Password Reset Email
        // ==============================================================
        public async Task<bool> SendPasswordResetEmailAsync(string recipientEmail, string recipientName, string resetUrl)
        {
            try
            {
                var (smtpClient, senderEmail, senderName) = GetSmtpClient();

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = "Reset Your HomeNest Airbnb Password",
                    Body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; background: #ffffff;'>
                            <div style='background: #0f172a; padding: 24px; text-align: center;'>
                                <h1 style='color: #ffffff; margin: 0; font-size: 24px; font-weight: 800;'>HomeNest Airbnb</h1>
                            </div>
                            <div style='padding: 30px; color: #334155;'>
                                <h2 style='color: #0f172a; margin-top: 0;'>Password Reset Request</h2>
                                <p>Hello <b>{recipientName}</b>,</p>
                                <p>We received a request to reset your account password. Click the secure link below to set a new password:</p>
                                <div style='text-align: center; margin: 25px 0;'>
                                    <a href='{resetUrl}' style='display:inline-block;padding:12px 28px;background:#ff385c;color:#ffffff;text-decoration:none;border-radius:8px;font-weight:bold;'>Reset Password Now</a>
                                </div>
                                <p style='color: #64748b; font-size: 12px;'>Link valid for 1 hour. If the button above does not work, copy and paste this URL into your browser:<br/><span style='word-break:break-all;'>{resetUrl}</span></p>
                                <hr style='border: none; border-top: 1px solid #f1f5f9; margin: 25px 0;' />
                                <p style='color: #94a3b8; font-size: 12px;'>If you did not make this request, you can safely ignore this email.</p>
                            </div>
                        </div>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(recipientEmail);
                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Password reset email sent to {Email}", recipientEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send password reset email to {Email}.", recipientEmail);
                return false;
            }
        }

        // ==============================================================
        // 3. Booking Confirmation & Receipt Email
        // ==============================================================
        public async Task<bool> SendBookingConfirmationAsync(string recipientEmail, string recipientName, string bookingRef, string propertyTitle, decimal amount, string checkInDate, string checkOutDate)
        {
            try
            {
                var (smtpClient, senderEmail, senderName) = GetSmtpClient();

                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = $"Reservation Confirmed: {bookingRef} - {propertyTitle}",
                    Body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 12px; overflow: hidden; background: #ffffff;'>
                            <div style='background: #059669; padding: 24px; text-align: center;'>
                                <h1 style='color: #ffffff; margin: 0; font-size: 24px; font-weight: 800;'>Booking Confirmed!</h1>
                            </div>
                            <div style='padding: 30px; color: #334155;'>
                                <p>Hello <b>{recipientName}</b>,</p>
                                <p>Your reservation on <b>Airbnb Booking System</b> is officially confirmed. Here are your trip details:</p>
                                <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 18px; margin: 20px 0;'>
                                    <p style='margin: 4px 0;'><strong>Booking Reference:</strong> {bookingRef}</p>
                                    <p style='margin: 4px 0;'><strong>Property:</strong> {propertyTitle}</p>
                                    <p style='margin: 4px 0;'><strong>Check-In Date:</strong> {checkInDate}</p>
                                    <p style='margin: 4px 0;'><strong>Check-Out Date:</strong> {checkOutDate}</p>
                                    <p style='margin: 4px 0; color: #059669; font-size: 16px; font-weight: bold;'><strong>Total Paid:</strong> RM {amount:N2}</p>
                                </div>
                                <p style='font-size: 13px; color: #64748b;'>You can access your Digital QR Check-In Pass and full receipt in your Guest Trips dashboard.</p>
                            </div>
                        </div>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(recipientEmail);
                await smtpClient.SendMailAsync(mailMessage);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send booking confirmation email to {Email}.", recipientEmail);
                return false;
            }
        }
    }
}

