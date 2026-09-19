USE [AssistantCoreDb];

-- Runtime database principals are provisioned only when the deployment supplies
-- non-zero Microsoft Entra client IDs through Flyway placeholders. Local and
-- legacy environments keep the zero defaults and skip this block.

DECLARE @apiClientId uniqueidentifier = '${API_CLIENT_ID}';
DECLARE @workerClientId uniqueidentifier = '${WORKER_CLIENT_ID}';
DECLARE @zero uniqueidentifier = '00000000-0000-0000-0000-000000000000';
DECLARE @sql nvarchar(max);

IF @apiClientId <> @zero
BEGIN
    DECLARE @apiName sysname = '${API_IDENTITY_NAME}';
    DECLARE @apiSidHex nvarchar(34) = sys.fn_varbintohexstr(CONVERT(varbinary(16), @apiClientId));

    IF DATABASE_PRINCIPAL_ID(@apiName) IS NULL
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@apiName) + N' WITH SID = ' + @apiSidHex + N', TYPE = E;';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members drm
        JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
        JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
        WHERE role_principal.name = 'db_datareader' AND member_principal.name = @apiName)
    BEGIN
        SET @sql = N'ALTER ROLE [db_datareader] ADD MEMBER ' + QUOTENAME(@apiName) + N';';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members drm
        JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
        JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
        WHERE role_principal.name = 'db_datawriter' AND member_principal.name = @apiName)
    BEGIN
        SET @sql = N'ALTER ROLE [db_datawriter] ADD MEMBER ' + QUOTENAME(@apiName) + N';';
        EXEC sys.sp_executesql @sql;
    END;
END;

IF @workerClientId <> @zero
BEGIN
    DECLARE @workerName sysname = '${WORKER_IDENTITY_NAME}';
    DECLARE @workerSidHex nvarchar(34) = sys.fn_varbintohexstr(CONVERT(varbinary(16), @workerClientId));

    IF DATABASE_PRINCIPAL_ID(@workerName) IS NULL
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@workerName) + N' WITH SID = ' + @workerSidHex + N', TYPE = E;';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members drm
        JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
        JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
        WHERE role_principal.name = 'db_datareader' AND member_principal.name = @workerName)
    BEGIN
        SET @sql = N'ALTER ROLE [db_datareader] ADD MEMBER ' + QUOTENAME(@workerName) + N';';
        EXEC sys.sp_executesql @sql;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members drm
        JOIN sys.database_principals role_principal ON role_principal.principal_id = drm.role_principal_id
        JOIN sys.database_principals member_principal ON member_principal.principal_id = drm.member_principal_id
        WHERE role_principal.name = 'db_datawriter' AND member_principal.name = @workerName)
    BEGIN
        SET @sql = N'ALTER ROLE [db_datawriter] ADD MEMBER ' + QUOTENAME(@workerName) + N';';
        EXEC sys.sp_executesql @sql;
    END;
END;
