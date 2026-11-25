BEGIN TRANSACTION;
CREATE TABLE [DataProtectionKeys] (
    [Id] int NOT NULL IDENTITY,
    [FriendlyName] nvarchar(max) NULL,
    [Xml] nvarchar(max) NULL,
    CONSTRAINT [PK_DataProtectionKeys] PRIMARY KEY ([Id])
);

INSERT INTO [__EFMigrationsHistory_SessionTickets] ([MigrationId], [ProductVersion])
VALUES (N'20261125063008_AddDataProtectionKeys', N'10.0.15');

COMMIT;
GO

