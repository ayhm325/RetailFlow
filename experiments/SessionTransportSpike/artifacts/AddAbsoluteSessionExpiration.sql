BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_SessionTickets]
    WHERE [MigrationId] = N'20261125072108_AddAbsoluteSessionExpiration'
)
BEGIN
    ALTER TABLE [AuthenticationSessionTickets] ADD [AbsoluteExpiresUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_SessionTickets]
    WHERE [MigrationId] = N'20261125072108_AddAbsoluteSessionExpiration'
)
BEGIN
    UPDATE [AuthenticationSessionTickets]
    SET [AbsoluteExpiresUtc] = DATEADD(HOUR, 8, [CreatedUtc])
    WHERE [AbsoluteExpiresUtc] IS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_SessionTickets]
    WHERE [MigrationId] = N'20261125072108_AddAbsoluteSessionExpiration'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AuthenticationSessionTickets]') AND [c].[name] = N'AbsoluteExpiresUtc');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [AuthenticationSessionTickets] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [AuthenticationSessionTickets] ALTER COLUMN [AbsoluteExpiresUtc] datetimeoffset NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_SessionTickets]
    WHERE [MigrationId] = N'20261125072108_AddAbsoluteSessionExpiration'
)
BEGIN
    CREATE INDEX [IX_AuthenticationSessionTickets_AbsoluteExpiresUtc] ON [AuthenticationSessionTickets] ([AbsoluteExpiresUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_SessionTickets]
    WHERE [MigrationId] = N'20261125072108_AddAbsoluteSessionExpiration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_SessionTickets] ([MigrationId], [ProductVersion])
    VALUES (N'20261125072108_AddAbsoluteSessionExpiration', N'10.0.15');
END;

COMMIT;
GO

