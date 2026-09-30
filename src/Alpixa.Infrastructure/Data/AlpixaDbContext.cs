using Alpixa.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Alpixa.Infrastructure.Data;

public class AlpixaDbContext(DbContextOptions<AlpixaDbContext> options) : DbContext(options)
{
    public DbSet<SenderProfile> SenderProfiles => Set<SenderProfile>();
    public DbSet<ContactList> ContactLists => Set<ContactList>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<SuppressionEntry> Suppressions => Set<SuppressionEntry>();
    public DbSet<EmailTemplate> Templates => Set<EmailTemplate>();
    public DbSet<TemplateAttachment> TemplateAttachments => Set<TemplateAttachment>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<SendJob> SendJobs => Set<SendJob>();
    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SenderProfile>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Ignore(p => p.FromDomain);
            e.Ignore(p => p.SmtpSecretKey);
            e.Ignore(p => p.ImapSecretKey);
            e.Ignore(p => p.OAuthSecretKey);
        });

        b.Entity<ContactList>(e =>
        {
            e.HasMany(l => l.Contacts).WithOne().HasForeignKey(c => c.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Contact>(e =>
        {
            e.HasIndex(c => new { c.ListId, c.EmailNormalized }).IsUnique();
            e.HasIndex(c => c.EmailNormalized);
            e.HasIndex(c => new { c.ListId, c.Status });
            e.HasIndex(c => c.Domain);
        });

        b.Entity<SuppressionEntry>(e => e.HasIndex(s => s.EmailNormalized).IsUnique());

        b.Entity<EmailTemplate>(e =>
            e.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TemplateId).OnDelete(DeleteBehavior.Cascade));

        b.Entity<Campaign>(e => e.HasIndex(c => c.Status));

        b.Entity<SendJob>(e =>
        {
            e.HasIndex(j => new { j.CampaignId, j.ContactId }).IsUnique();
            e.HasIndex(j => new { j.CampaignId, j.Status, j.Sequence });
            e.HasIndex(j => j.Email);
            e.HasIndex(j => j.SentUtc);
            e.HasIndex(j => j.MessageId);
        });

        b.Entity<DeliveryEvent>(e =>
        {
            e.HasIndex(d => d.EmailNormalized);
            e.HasIndex(d => new { d.CampaignId, d.Kind });
        });

        b.Entity<AppSetting>(e => e.HasKey(s => s.Key));
    }
}

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AlpixaDbContext>
{
    public AlpixaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AlpixaDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AlpixaDbContext(options);
    }
}
