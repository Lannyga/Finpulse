-- PROPUESTA NO EJECUTADA. Requiere nueva aprobacion administrativa.
-- Cuenta autorizada: SYS AS SYSDBA mediante autenticacion Windows local.
-- No crear cuentas ni objetos, no cambiar contrasenas, no modificar FINPULSE_APP.
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET ECHO OFF VERIFY OFF DEFINE OFF
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE
    n NUMBER;
BEGIN
    IF USER <> 'SYS' OR SYS_CONTEXT('USERENV','CON_NAME') <> 'FREEPDB1' THEN
        RAISE_APPLICATION_ERROR(-20011, 'Cuenta administrativa o contenedor incorrectos.');
    END IF;
    SELECT COUNT(*) INTO n FROM DBA_USERS WHERE USERNAME = 'FINPULSE_WEB';
    IF n <> 1 THEN RAISE_APPLICATION_ERROR(-20012, 'FINPULSE_WEB no existe. No se concedieron permisos.'); END IF;
    SELECT COUNT(*) INTO n FROM DBA_SYS_PRIVS
    WHERE GRANTEE = 'FINPULSE_WEB' AND PRIVILEGE = 'CREATE SEQUENCE';
    IF n = 0 THEN EXECUTE IMMEDIATE 'GRANT CREATE SEQUENCE TO FINPULSE_WEB'; END IF;
END;
/
-- Despues de instalar y verificar las seis tablas, proponer/aprobar por separado:
-- REVOKE CREATE TABLE, CREATE SEQUENCE FROM FINPULSE_WEB;
-- FINPULSE_WEB_TEST todavia no existe; su creacion es una operacion distinta.
