SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
SET NOCOUNT ON;
SET XACT_ABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @StoreA uniqueidentifier = NEWID();
    DECLARE @StoreB uniqueidentifier = NEWID();

    INSERT INTO dbo.Stores (Id, Name, Slug, IsActive)
    VALUES
        (@StoreA, N'Spike Store A', N'spike-store-a', 1),
        (@StoreB, N'Spike Store B', N'spike-store-b', 1);

    PRINT 'TEST 1: Same email in different stores';

    INSERT INTO dbo.AspNetUsers
    (
        Id, AccountKind, StoreId,
        UserName, NormalizedUserName,
        Email, NormalizedEmail,
        EmailConfirmed, PhoneNumberConfirmed,
        TwoFactorEnabled, LockoutEnabled, AccessFailedCount
    )
    VALUES
    (
        N'spike-customer-a', 2, @StoreA,
        N'spike-customer-a', N'SPIKE-CUSTOMER-A',
        N'same@spike.invalid', N'SAME@SPIKE.INVALID',
        0, 0, 0, 1, 0
    ),
    (
        N'spike-customer-b', 2, @StoreB,
        N'spike-customer-b', N'SPIKE-CUSTOMER-B',
        N'same@spike.invalid', N'SAME@SPIKE.INVALID',
        0, 0, 0, 1, 0
    );

    PRINT 'PASS: Same email accepted across stores';

    PRINT 'TEST 2: Duplicate customer email in same store';

    DECLARE @CustomerDuplicateRejected bit = 0;

    BEGIN TRY
        INSERT INTO dbo.AspNetUsers
        (
            Id, AccountKind, StoreId,
            UserName, NormalizedUserName,
            Email, NormalizedEmail,
            EmailConfirmed, PhoneNumberConfirmed,
            TwoFactorEnabled, LockoutEnabled, AccessFailedCount
        )
        VALUES
        (
            N'spike-customer-duplicate', 2, @StoreA,
            N'spike-customer-duplicate',
            N'SPIKE-CUSTOMER-DUPLICATE',
            N'same@spike.invalid', N'SAME@SPIKE.INVALID',
            0, 0, 0, 1, 0
        );
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2601, 2627)
            SET @CustomerDuplicateRejected = 1;
        ELSE
            THROW;
    END CATCH;

    IF @CustomerDuplicateRejected = 0
        THROW 51001, 'Duplicate customer email was accepted.', 1;

    PRINT 'PASS: Duplicate customer email rejected';

    PRINT 'TEST 3: Duplicate BackOffice email';

    INSERT INTO dbo.AspNetUsers
    (
        Id, AccountKind, StoreId,
        UserName, NormalizedUserName,
        Email, NormalizedEmail,
        EmailConfirmed, PhoneNumberConfirmed,
        TwoFactorEnabled, LockoutEnabled, AccessFailedCount
    )
    VALUES
    (
        N'spike-backoffice-a', 1, NULL,
        N'spike-backoffice-a', N'SPIKE-BACKOFFICE-A',
        N'admin@spike.invalid', N'ADMIN@SPIKE.INVALID',
        0, 0, 0, 1, 0
    );

    DECLARE @BackOfficeDuplicateRejected bit = 0;

    BEGIN TRY
        INSERT INTO dbo.AspNetUsers
        (
            Id, AccountKind, StoreId,
            UserName, NormalizedUserName,
            Email, NormalizedEmail,
            EmailConfirmed, PhoneNumberConfirmed,
            TwoFactorEnabled, LockoutEnabled, AccessFailedCount
        )
        VALUES
        (
            N'spike-backoffice-b', 1, NULL,
            N'spike-backoffice-b', N'SPIKE-BACKOFFICE-B',
            N'admin@spike.invalid', N'ADMIN@SPIKE.INVALID',
            0, 0, 0, 1, 0
        );
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() IN (2601, 2627)
            SET @BackOfficeDuplicateRejected = 1;
        ELSE
            THROW;
    END CATCH;

    IF @BackOfficeDuplicateRejected = 0
        THROW 51002, 'Duplicate BackOffice email was accepted.', 1;

    PRINT 'PASS: Duplicate BackOffice email rejected';

    PRINT 'TEST 4: Same email in BackOffice and StoreCustomer';

    INSERT INTO dbo.AspNetUsers
    (
        Id, AccountKind, StoreId,
        UserName, NormalizedUserName,
        Email, NormalizedEmail,
        EmailConfirmed, PhoneNumberConfirmed,
        TwoFactorEnabled, LockoutEnabled, AccessFailedCount
    )
    VALUES
    (
        N'spike-customer-admin-email', 2, @StoreA,
        N'spike-customer-admin-email',
        N'SPIKE-CUSTOMER-ADMIN-EMAIL',
        N'admin@spike.invalid', N'ADMIN@SPIKE.INVALID',
        0, 0, 0, 1, 0
    );

    PRINT 'PASS: Same email accepted across account kinds';

    PRINT 'TEST 5: Invalid AccountKind and StoreId';

    DECLARE @InvalidBackOfficeRejected bit = 0;

    BEGIN TRY
        INSERT INTO dbo.AspNetUsers
        (
            Id, AccountKind, StoreId,
            Email, NormalizedEmail,
            EmailConfirmed, PhoneNumberConfirmed,
            TwoFactorEnabled, LockoutEnabled, AccessFailedCount
        )
        VALUES
        (
            N'spike-invalid-backoffice', 1, @StoreA,
            N'invalid-a@spike.invalid',
            N'INVALID-A@SPIKE.INVALID',
            0, 0, 0, 1, 0
        );
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
            SET @InvalidBackOfficeRejected = 1;
        ELSE
            THROW;
    END CATCH;

    IF @InvalidBackOfficeRejected = 0
        THROW 51003, 'Invalid BackOffice combination was accepted.', 1;

    PRINT 'PASS: BackOffice with StoreId rejected';

    DECLARE @InvalidCustomerRejected bit = 0;

    BEGIN TRY
        INSERT INTO dbo.AspNetUsers
        (
            Id, AccountKind, StoreId,
            Email, NormalizedEmail,
            EmailConfirmed, PhoneNumberConfirmed,
            TwoFactorEnabled, LockoutEnabled, AccessFailedCount
        )
        VALUES
        (
            N'spike-invalid-customer', 2, NULL,
            N'invalid-b@spike.invalid',
            N'INVALID-B@SPIKE.INVALID',
            0, 0, 0, 1, 0
        );
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() = 547
            SET @InvalidCustomerRejected = 1;
        ELSE
            THROW;
    END CATCH;

    IF @InvalidCustomerRejected = 0
        THROW 51004, 'Invalid StoreCustomer combination was accepted.', 1;

    PRINT 'PASS: StoreCustomer without StoreId rejected';

    ROLLBACK TRANSACTION;

    PRINT '============================================';
    PRINT 'ALL DATABASE CONSTRAINT TESTS PASSED';
    PRINT 'All test data has been rolled back.';
    PRINT '============================================';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;