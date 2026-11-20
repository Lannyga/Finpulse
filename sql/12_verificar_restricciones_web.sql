-- Pruebas sinteticas con rollback, exclusivamente como propietario FINPULSE_WEB_TEST.
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
SET ECHO OFF VERIFY OFF DEFINE ON SERVEROUTPUT ON
VARIABLE qa_name VARCHAR2(40)
BEGIN :qa_name := '&1'; END;
/
SET DEFINE OFF
DECLARE
    student NUMBER; other_student NUMBER; income_category NUMBER; expense_category NUMBER; goal NUMBER;
    existing_key NVARCHAR2(32); rollback_key NVARCHAR2(32) := RAWTOHEX(SYS_GUID());
    before_count NUMBER; after_count NUMBER; balance NUMBER; reserved NUMBER; warning_value NUMBER;
    PROCEDURE reject(expected NUMBER, owner_id NUMBER, kind NUMBER, category_id NUMBER, goal_id NUMBER, amount NUMBER, request_key NVARCHAR2) IS
        code NUMBER;
    BEGIN
        SAVEPOINT invalid_row;
        BEGIN
            INSERT INTO "MOVIMIENTOS" ("UserId","Kind","CategoryId","GoalId","Amount","Date","RequestKey")
            VALUES (owner_id,kind,category_id,goal_id,amount,TRUNC(SYSDATE),request_key);
            code:=0;
        EXCEPTION WHEN OTHERS THEN code:=SQLCODE;
        END;
        ROLLBACK TO invalid_row;
        IF code<>expected THEN RAISE_APPLICATION_ERROR(-20030,'Restriccion inesperada. SQLCODE='||code); END IF;
        DBMS_OUTPUT.PUT_LINE('Restriccion verificada SQLCODE='||code);
    END;
BEGIN
    IF USER<>'FINPULSE_WEB_TEST' OR SYS_CONTEXT('USERENV','CON_NAME')<>'FREEPDB1' THEN
        RAISE_APPLICATION_ERROR(-20031,'Solo se permite el esquema de pruebas en FREEPDB1.');
    END IF;
    IF NOT REGEXP_LIKE(:qa_name,'^qa_[a-f0-9]{12}$') THEN RAISE_APPLICATION_ERROR(-20032,'Cuenta sintetica invalida.'); END IF;
    SELECT "Id","WarningPercent" INTO student,warning_value FROM "USUARIOS" WHERE "Username"=:qa_name;
    SELECT "Id" INTO other_student FROM "USUARIOS" WHERE "Username"=:qa_name||'b';
    SELECT MIN("Id") INTO income_category FROM "CATEGORIAS" WHERE "Kind"=0;
    SELECT MIN("Id") INTO expense_category FROM "CATEGORIAS" WHERE "Kind"=1;
    SELECT "Id" INTO goal FROM "METAS_AHORRO" WHERE "UserId"=student;
    SELECT "RequestKey" INTO existing_key FROM "MOVIMIENTOS" WHERE "UserId"=student AND "Kind"=0;
    SELECT COUNT(*),SUM(CASE "Kind" WHEN 0 THEN "Amount" WHEN 1 THEN -"Amount" ELSE 0 END),SUM(CASE "Kind" WHEN 2 THEN "Amount" ELSE 0 END)
    INTO before_count,balance,reserved FROM "MOVIMIENTOS" WHERE "UserId"=student;
    IF balance<>750 OR reserved<>500 OR warning_value<>75 THEN RAISE_APPLICATION_ERROR(-20033,'Persistencia HTTP inconsistente.'); END IF;
    reject(-2290,student,0,income_category,NULL,-1,RAWTOHEX(SYS_GUID()));
    reject(-2291,student,0,expense_category,NULL,1,RAWTOHEX(SYS_GUID()));
    reject(-2291,other_student,2,NULL,goal,1,RAWTOHEX(SYS_GUID()));
    reject(-1,student,0,income_category,NULL,1,existing_key);
    SAVEPOINT valid_row;
    INSERT INTO "MOVIMIENTOS" ("UserId","Kind","CategoryId","Amount","Date","RequestKey")
    VALUES (student,0,income_category,1,TRUNC(SYSDATE),rollback_key);
    ROLLBACK TO valid_row;
    SELECT COUNT(*) INTO after_count FROM "MOVIMIENTOS" WHERE "RequestKey"=rollback_key;
    IF after_count<>0 THEN RAISE_APPLICATION_ERROR(-20034,'Fallo de rollback.'); END IF;
    SELECT COUNT(*) INTO after_count FROM "MOVIMIENTOS" WHERE "UserId"=student;
    IF after_count<>before_count THEN RAISE_APPLICATION_ERROR(-20035,'Las pruebas cambiaron filas persistentes.'); END IF;
    ROLLBACK;
    DBMS_OUTPUT.PUT_LINE('OK: persistencia SQL directa, CHECK, FK, aislamiento de meta, unicidad y rollback. Sin cambios persistentes.');
END;
/
EXIT
