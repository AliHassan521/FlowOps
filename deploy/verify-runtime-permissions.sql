SET NOCOUNT ON;
EXECUTE AS LOGIN = 'FlowOpsRuntime';
IF COALESCE(IS_SRVROLEMEMBER('sysadmin'), -1) <> 0 OR COALESCE(IS_MEMBER('db_owner'), -1) <> 0
    THROW 51000, 'Runtime identity must not own the server or database.', 1;
IF COALESCE(HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE'), -1) <> 0
    THROW 51000, 'Runtime identity must not have schema permissions.', 1;
IF COALESCE(HAS_PERMS_BY_NAME('dbo.AuditEvents', 'OBJECT', 'UPDATE'), -1) <> 0
   OR COALESCE(HAS_PERMS_BY_NAME('dbo.AuditEvents', 'OBJECT', 'DELETE'), -1) <> 0
   OR COALESCE(HAS_PERMS_BY_NAME('dbo.__EFMigrationsHistory', 'OBJECT', 'INSERT'), -1) <> 0
    THROW 51000, 'Audit/migration history must be protected from runtime rewrites.', 1;
IF COALESCE(HAS_PERMS_BY_NAME('dbo.AuditEvents', 'OBJECT', 'INSERT'), -1) <> 1
   OR COALESCE(HAS_PERMS_BY_NAME('dbo.WorkItems', 'OBJECT', 'UPDATE'), -1) <> 1
    THROW 51000, 'Runtime identity requires ordinary business writes and audit append.', 1;
REVERT;
PRINT 'Runtime data permissions and audit/history protections verified.';
