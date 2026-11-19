-- EJECUTAR CON ADMINISTRADOR CONECTADO A FREEPDB1 (NO EN CDB$ROOT)
-- Ejemplo: sqlplus system@localhost:1521/FREEPDB1
-- Crear cuenta de aplicación con permisos limitados para esta etapa.
SET VERIFY OFF
ACCEPT clave_finpulse CHAR PROMPT 'Clave para FINPULSE_APP: ' HIDE
CREATE USER FINPULSE_APP IDENTIFIED BY "&clave_finpulse" DEFAULT TABLESPACE USERS QUOTA 100M ON USERS;
GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE TO FINPULSE_APP;
UNDEFINE clave_finpulse
PROMPT Usuario FINPULSE_APP creado. Ahora conéctate con ese usuario y ejecuta 02_esquema_usuarios.sql
