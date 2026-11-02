using Microsoft.EntityFrameworkCore;
namespace TLS.Data;

public sealed class TLSDbContext(DbContextOptions<TLSDbContext> options) : DbContext(options)
{
    public DbSet<IdSequence> IdSequences=>Set<IdSequence>();
    public DbSet<ImportIdMapping> ImportIdMappings=>Set<ImportIdMapping>();
    public DbSet<EventRecord> Events => Set<EventRecord>();
    public DbSet<ProgramRecord> Programs => Set<ProgramRecord>();
    public DbSet<ParticipantRecord> Participants => Set<ParticipantRecord>();
    public DbSet<EntryRecord> ProgramEntries => Set<EntryRecord>();
    public DbSet<EntryMember> EntryMembers => Set<EntryMember>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EventImport> EventImports => Set<EventImport>();
    public DbSet<MatchLiveRecord> MatchLiveRecords=>Set<MatchLiveRecord>();
    public DbSet<EventOperation> EventOperations => Set<EventOperation>();
    public DbSet<ProgramOperation> ProgramOperations => Set<ProgramOperation>();
    public DbSet<SbaRanking> SbaRankings => Set<SbaRanking>();
    public DbSet<BadmintonClub> BadmintonClubs => Set<BadmintonClub>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<IdSequence>().ToTable("IdSequences",t=>t.HasCheckConstraint("CK_IdSequences_Value","[Value]>=0")).HasKey(x=>x.Key);
        b.Entity<IdSequence>().Property(x=>x.Key).HasMaxLength(160).UseCollation("Latin1_General_100_BIN2");
        b.Entity<ImportIdMapping>().ToTable("ImportIdMappings").HasKey(x=>x.Fingerprint);
        b.Entity<ImportIdMapping>().Property(x=>x.Fingerprint).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
        b.Entity<ImportIdMapping>().Property(x=>x.Kind).HasMaxLength(24);
        b.Entity<ImportIdMapping>().HasIndex(x=>x.LocalId).IsUnique();
        b.Entity<MatchLiveRecord>().ToTable("MatchLiveRecords",t=>t.HasCheckConstraint("CK_MatchLiveRecords_Status","[Status] IN ('SC','IP','RC','C','WO')")).HasKey(x=>x.MatchId);
        b.Entity<MatchLiveRecord>().HasOne<ProgramRecord>().WithMany().HasForeignKey(x=>x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EventOperation>().ToTable("EventOperations").HasKey(x=>x.EventId);
        b.Entity<ProgramOperation>().ToTable("ProgramOperations").HasKey(x=>x.ProgramId);
        b.Entity<EventOperation>().HasOne<EventRecord>().WithMany().HasForeignKey(x=>x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ProgramOperation>().HasOne<ProgramRecord>().WithMany().HasForeignKey(x=>x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EventImport>().ToTable("EventImports");
        b.Entity<EventImport>().HasKey(x=>x.EventId);
        b.Entity<EventImport>().HasOne<EventRecord>().WithMany().HasForeignKey(x=>x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EventImport>().Property(x=>x.Stage).HasMaxLength(8);
        b.UseCollation("Latin1_General_100_BIN2");
        b.Entity<EventRecord>().ToTable("Events");
        b.Entity<ProgramRecord>().ToTable("Programs");
        b.Entity<ParticipantRecord>().ToTable("Participants");
        b.Entity<EntryRecord>().ToTable("ProgramEntries");
        b.Entity<EntryMember>().ToTable("EntryMembers");
        b.Entity<SystemSetting>().ToTable("SystemSettings");
        b.Entity<AuditLog>().ToTable("AuditLogs");
        b.Entity<EntryMember>().HasKey(x => new { x.EntryId, x.Position });
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            if (property.ClrType == typeof(string) && (property.Name.EndsWith("Id") || property.Name == "Id")) { property.SetMaxLength(160); property.SetCollation("Latin1_General_100_BIN2"); }
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
            if (property.Name == "Status") property.SetMaxLength(8);
        b.Entity<SystemSetting>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<AuditLog>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<SystemSetting>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<ProgramRecord>().HasOne<EventRecord>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ParticipantRecord>().HasOne<EventRecord>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EntryRecord>().HasOne<EventRecord>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EntryRecord>().HasOne<ProgramRecord>().WithMany().HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EntryMember>().HasOne<EntryRecord>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EntryMember>().HasOne<ParticipantRecord>().WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<EventRecord>().ToTable(t => t.HasCheckConstraint("CK_Events_Status", "[Status] IN ('D','A','C','X')"));
        b.Entity<ProgramRecord>().ToTable(t => t.HasCheckConstraint("CK_Programs_Status", "[Status] IN ('A','X')"));
        b.Entity<EntryRecord>().ToTable(t => t.HasCheckConstraint("CK_ProgramEntries_Status", "[Status] IN ('A','X')"));
        b.Entity<SystemSetting>().Property(x=>x.LiveVenueJson).HasDefaultValue("{}");
        b.Entity<SystemSetting>().HasData(new SystemSetting());
        b.Entity<SbaRanking>().ToTable("SbaRankings");
        // SbaRankings
        b.Entity<SbaRanking>(e => {
            e.HasKey(x => x.SbaRankingId).HasName("PK_SbaRankings");
            e.Property(x => x.SbaRankingId).HasColumnName("SbaRankingID");
            e.Property(x => x.RankingType).HasMaxLength(80);
            e.Property(x => x.Player1SbaId).HasColumnName("Player1SbaID").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Player1Name).HasMaxLength(200);
            e.Property(x => x.Player1Club).HasMaxLength(200);
            e.Property(x => x.Player2SbaId).HasColumnName("Player2SbaID").HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Player2Name).HasMaxLength(200);
            e.Property(x => x.Player2Club).HasMaxLength(200);
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("(sysutcdatetime())");
            // Filtered unique index for doubles pairs (Player2SbaID IS NOT NULL)
            e.HasIndex(x => new { x.RankingType, x.Player1SbaId, x.Player2SbaId }).IsUnique()
             .HasDatabaseName("UX_SbaRankings_Type_Players")
             .HasFilter("[Player2SbaID] IS NOT NULL");
            // Filtered unique index for singles (Player2SbaID IS NULL)
            e.HasIndex(x => new { x.RankingType, x.Player1SbaId }).IsUnique()
             .HasDatabaseName("UX_SbaRankings_Type_Singles")
             .HasFilter("[Player2SbaID] IS NULL");
        });

        b.Entity<BadmintonClub>(e => {
            e.ToTable("BadmintonClub");
            e.HasKey(x => x.ClubId);
            e.Property(x => x.ClubId).HasColumnName("ClubId");
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.ContactNumber).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Country).HasMaxLength(100);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("(getdate())");
        });
    }
}
