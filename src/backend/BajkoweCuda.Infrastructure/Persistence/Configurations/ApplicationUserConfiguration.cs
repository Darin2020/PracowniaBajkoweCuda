using BajkoweCuda.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BajkoweCuda.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(ApplicationUser.DisplayNameMaxLength);
        builder.Property(user => user.PreferredLanguage)
            .HasMaxLength(ApplicationUser.PreferredLanguageMaxLength)
            .IsUnicode(false);
        builder.Property(user => user.TermsVersion)
            .HasMaxLength(ApplicationUser.TermsVersionMaxLength)
            .IsUnicode(false);
    }
}
