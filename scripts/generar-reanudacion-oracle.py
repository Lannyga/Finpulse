"""Genera SQL de reanudacion verificable desde el DDL canonico; nunca se conecta a Oracle."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent
source = (root / 'sql/05_esquema_web_oracle.sql').read_text(encoding='utf-8')
tables = []
for ddl in re.findall(r"EXECUTE IMMEDIATE '(CREATE TABLE\s+.*?)';", source, re.S):
    name = re.search(r'CREATE TABLE\s+"([^"]+)"', ddl)[1]
    columns, constraints = [], []
    for line in ddl.splitlines()[2:]:
        match = re.match(r'\s*"([^"]+)" (NUMBER\((\d+)(?:,(\d+))?\)|NVARCHAR2\((\d+)\)|TIMESTAMP\((\d+)\))(.*)', line)
        if match:
            n, kind, precision, scale, length, timestamp, tail = match.groups()
            columns.append(dict(n=n, t='NUMBER' if precision else 'NVARCHAR2' if length else f'TIMESTAMP({timestamp})', p=int(precision or 0), s=int(scale or 0), l=int(length or 0), nullable='N' if 'NOT NULL' in tail else 'Y', identity='YES' if 'IDENTITY' in tail else 'NO'))
        match = re.match(r'\s*CONSTRAINT "([^"]+)" (.*?)(?:,)?$', line)
        if match:
            n, body = match.groups()
            c = dict(n=n, t='P' if body.startswith('PRIMARY') else 'U' if body.startswith('UNIQUE') else 'R' if body.startswith('FOREIGN') else 'C')
            if c['t'] == 'C':
                c['expr'] = body[len('CHECK ('):-1]
            else:
                c['cols'] = ','.join(re.findall(r'"([^"]+)"', re.search(r'\(([^)]+)\)', body)[1]))
                if c['t'] == 'R':
                    ref = re.search(r'REFERENCES "([^"]+)" \(([^)]+)\)', body)
                    c.update(ref=ref[1], refcols=','.join(re.findall(r'"([^"]+)"', ref[2])))
            constraints.append(c)
    assert columns and constraints, name
    tables.append(dict(n=name, ddl=ddl, cols=columns, constraints=constraints))
indexes = []
for unique, name, table, cols in re.findall(r'CREATE (UNIQUE )?INDEX "([^"]+)" ON "([^"]+)" \(([^)]+)\);', source):
    indexes.append(dict(n=name, table=table, cols=','.join(re.findall(r'"([^"]+)"', cols)), unique='UNIQUE' if unique else 'NONUNIQUE'))
assert len(tables) == 6 and len(indexes) == 11

header = r'''-- GENERADO por scripts/generar-reanudacion-oracle.py; no editar manualmente.
-- PLAN no ejecuta DDL. APLICAR exige autorizacion administrativa previa.
-- Conserva objetos existentes compatibles y cancela ante diferencias; nunca DROP/REPLACE/ALTER.
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
SET ECHO OFF VERIFY OFF DEFINE ON SERVEROUTPUT ON SQLBLANKLINES ON
VARIABLE fp_mode VARCHAR2(16)
BEGIN :fp_mode := UPPER('&1'); END;
/
SET DEFINE OFF
DECLARE
  n NUMBER;
  mode_name VARCHAR2(16) := :fp_mode;
  manifest JSON_ARRAY_T := JSON_ARRAY_T.parse(q'~__MANIFEST__~');
  index_manifest JSON_ARRAY_T := JSON_ARRAY_T.parse(q'~__INDEXES__~');
  item JSON_OBJECT_T;
  expected JSON_OBJECT_T;
  arr JSON_ARRAY_T;
  ddl_text VARCHAR2(32767);
  item_name VARCHAR2(128);
  allowed_object BOOLEAN;
  candidate JSON_OBJECT_T;
  key_item JSON_OBJECT_T;
  key_list JSON_ARRAY_T;
  PROCEDURE fail(message VARCHAR2) IS BEGIN RAISE_APPLICATION_ERROR(-20010,message); END;
  FUNCTION column_names(constraint_name_in VARCHAR2) RETURN VARCHAR2 IS
    result VARCHAR2(4000);
  BEGIN
    SELECT LISTAGG(column_name, ',') WITHIN GROUP (ORDER BY position) INTO result
    FROM USER_CONS_COLUMNS WHERE constraint_name=constraint_name_in;
    RETURN result;
  END;
  FUNCTION normal(expression_in VARCHAR2) RETURN VARCHAR2 IS
  BEGIN RETURN REGEXP_REPLACE(UPPER(expression_in),'[[:space:]"]',''); END;
  PROCEDURE validate_table(t JSON_OBJECT_T) IS
    table_name_in VARCHAR2(128) := t.get_string('n');
    cols JSON_ARRAY_T := t.get_array('cols');
    cons JSON_ARRAY_T := t.get_array('constraints');
    c JSON_OBJECT_T;
    actual USER_TAB_COLUMNS%ROWTYPE;
    actual_constraint USER_CONSTRAINTS%ROWTYPE;
    reference_table VARCHAR2(128);
    identity_type VARCHAR2(30);
    count_found NUMBER;
    column_name_in VARCHAR2(128);
    constraint_name_in VARCHAR2(128);
  BEGIN
    SELECT COUNT(*) INTO count_found FROM USER_TABLES WHERE table_name=table_name_in;
    IF count_found=0 THEN RETURN; END IF;
    SELECT COUNT(*) INTO count_found FROM USER_TAB_COLUMNS WHERE table_name=table_name_in;
    IF count_found<>cols.get_size() THEN fail('Columnas incompatibles: '||table_name_in); END IF;
    SELECT COUNT(*) INTO count_found FROM USER_CONSTRAINTS WHERE table_name=table_name_in AND generated='USER NAME';
    IF count_found<>cons.get_size() THEN fail('Restricciones inesperadas: '||table_name_in); END IF;
    FOR j IN 0..cols.get_size()-1 LOOP
      c:=TREAT(cols.get(j) AS JSON_OBJECT_T);
      column_name_in:=c.get_string('n');
      BEGIN
        SELECT * INTO actual FROM USER_TAB_COLUMNS WHERE table_name=table_name_in AND column_name=column_name_in;
      EXCEPTION WHEN NO_DATA_FOUND THEN fail('Columna ausente: '||table_name_in||'.'||c.get_string('n')); END;
      IF actual.column_id<>j+1 OR actual.data_type<>c.get_string('t') OR actual.nullable<>c.get_string('nullable')
        OR actual.identity_column<>c.get_string('identity') THEN fail('Tipo/orden/nulabilidad incompatible: '||table_name_in||'.'||actual.column_name); END IF;
      IF actual.data_type='NUMBER' AND (NVL(actual.data_precision,-1)<>c.get_number('p') OR NVL(actual.data_scale,-1)<>c.get_number('s')) THEN fail('Precision incompatible: '||table_name_in||'.'||actual.column_name); END IF;
      IF actual.data_type='NVARCHAR2' AND actual.char_length<>c.get_number('l') THEN fail('Longitud incompatible: '||table_name_in||'.'||actual.column_name); END IF;
      IF actual.identity_column='YES' THEN
        SELECT generation_type INTO identity_type FROM USER_TAB_IDENTITY_COLS WHERE table_name=table_name_in AND column_name=actual.column_name;
        IF identity_type NOT LIKE 'BY DEFAULT%' OR actual.default_on_null<>'YES' THEN fail('Identidad incompatible: '||table_name_in); END IF;
      END IF;
    END LOOP;
    FOR j IN 0..cons.get_size()-1 LOOP
      c:=TREAT(cons.get(j) AS JSON_OBJECT_T);
      constraint_name_in:=c.get_string('n');
      BEGIN
        SELECT * INTO actual_constraint FROM USER_CONSTRAINTS WHERE constraint_name=constraint_name_in;
      EXCEPTION WHEN NO_DATA_FOUND THEN fail('Restriccion ausente en tabla existente: '||c.get_string('n')); END;
      IF actual_constraint.table_name<>table_name_in OR actual_constraint.constraint_type<>c.get_string('t')
        OR actual_constraint.status<>'ENABLED' OR actual_constraint.validated<>'VALIDATED' OR actual_constraint.deferrable<>'NOT DEFERRABLE'
      THEN fail('Restriccion incompatible: '||c.get_string('n')); END IF;
      IF c.get_string('t')='C' THEN
        IF normal(actual_constraint.search_condition_vc)<>normal(c.get_string('expr')) THEN fail('CHECK incompatible: '||c.get_string('n')); END IF;
      ELSE
        IF column_names(actual_constraint.constraint_name)<>c.get_string('cols') THEN fail('Columnas de restriccion incompatibles: '||c.get_string('n')); END IF;
      END IF;
      IF c.get_string('t')='R' THEN
        IF actual_constraint.r_owner<>USER OR actual_constraint.delete_rule<>'NO ACTION' THEN fail('FK incompatible: '||c.get_string('n')); END IF;
        SELECT table_name INTO reference_table FROM USER_CONSTRAINTS WHERE constraint_name=actual_constraint.r_constraint_name;
        IF reference_table<>c.get_string('ref') OR column_names(actual_constraint.r_constraint_name)<>c.get_string('refcols') THEN fail('Destino FK incompatible: '||c.get_string('n')); END IF;
      END IF;
    END LOOP;
  END;
  PROCEDURE validate_index(i JSON_OBJECT_T) IS
    actual USER_INDEXES%ROWTYPE;
    names VARCHAR2(4000);
    index_name_in VARCHAR2(128) := i.get_string('n');
  BEGIN
    BEGIN SELECT * INTO actual FROM USER_INDEXES WHERE index_name=index_name_in;
    EXCEPTION WHEN NO_DATA_FOUND THEN RETURN; END;
    SELECT LISTAGG(column_name,',') WITHIN GROUP (ORDER BY column_position) INTO names FROM USER_IND_COLUMNS WHERE index_name=actual.index_name;
    IF actual.table_name<>i.get_string('table') OR actual.uniqueness<>i.get_string('unique') OR actual.status<>'VALID' OR names<>i.get_string('cols')
    THEN fail('Indice incompatible: '||actual.index_name); END IF;
  END;
BEGIN
  IF mode_name IS NULL OR mode_name NOT IN ('PLAN','APLICAR') THEN fail('Modo obligatorio: PLAN o APLICAR.'); END IF;
  IF USER NOT IN ('FINPULSE_WEB','FINPULSE_WEB_TEST') OR SYS_CONTEXT('USERENV','CON_NAME')<>'FREEPDB1' THEN fail('Usuario/contenedor incorrecto.'); END IF;
  FOR obj IN (SELECT object_name,object_type FROM USER_OBJECTS) LOOP
    allowed_object:=FALSE;
    FOR j IN 0..manifest.get_size()-1 LOOP
      candidate:=TREAT(manifest.get(j) AS JSON_OBJECT_T);
      IF obj.object_type='TABLE' AND obj.object_name=candidate.get_string('n') THEN allowed_object:=TRUE; END IF;
      key_list:=candidate.get_array('constraints');
      FOR k IN 0..key_list.get_size()-1 LOOP
        key_item:=TREAT(key_list.get(k) AS JSON_OBJECT_T);
        IF obj.object_type='INDEX' AND key_item.get_string('t') IN ('P','U') AND obj.object_name=key_item.get_string('n') THEN allowed_object:=TRUE; END IF;
      END LOOP;
    END LOOP;
    FOR j IN 0..index_manifest.get_size()-1 LOOP
      candidate:=TREAT(index_manifest.get(j) AS JSON_OBJECT_T);
      IF obj.object_type='INDEX' AND obj.object_name=candidate.get_string('n') THEN allowed_object:=TRUE; END IF;
    END LOOP;
    IF obj.object_type='SEQUENCE' THEN
      SELECT COUNT(*) INTO n FROM USER_TAB_IDENTITY_COLS WHERE sequence_name=obj.object_name;
      IF n=1 THEN allowed_object:=TRUE; END IF;
    END IF;
    IF NOT allowed_object THEN fail('Objeto inesperado: '||obj.object_type||' '||obj.object_name); END IF;
  END LOOP;
  -- Preflight completo antes del primer DDL. No ocultar un objeto incompatible detras de IF NOT EXISTS.
  FOR j IN 0..manifest.get_size()-1 LOOP validate_table(TREAT(manifest.get(j) AS JSON_OBJECT_T)); END LOOP;
  FOR j IN 0..index_manifest.get_size()-1 LOOP validate_index(TREAT(index_manifest.get(j) AS JSON_OBJECT_T)); END LOOP;
  FOR j IN 0..manifest.get_size()-1 LOOP
    item:=TREAT(manifest.get(j) AS JSON_OBJECT_T);
    item_name:=item.get_string('n');
    SELECT COUNT(*) INTO n FROM USER_TABLES WHERE table_name=item_name;
    IF n=0 THEN
      DBMS_OUTPUT.PUT_LINE(mode_name||' TABLE '||item.get_string('n'));
      IF mode_name='APLICAR' THEN
        ddl_text:=item.get_string('ddl');
        BEGIN EXECUTE IMMEDIATE ddl_text;
        EXCEPTION WHEN OTHERS THEN DBMS_OUTPUT.PUT_LINE('FALLO TABLE '||item.get_string('n')||' SQLCODE='||SQLCODE); RAISE; END;
      END IF;
    ELSE DBMS_OUTPUT.PUT_LINE('CONSERVADA TABLE '||item.get_string('n')); END IF;
  END LOOP;
  FOR j IN 0..index_manifest.get_size()-1 LOOP
    item:=TREAT(index_manifest.get(j) AS JSON_OBJECT_T);
    item_name:=item.get_string('n');
    SELECT COUNT(*) INTO n FROM USER_INDEXES WHERE index_name=item_name;
    IF n=0 THEN
      DBMS_OUTPUT.PUT_LINE(mode_name||' INDEX '||item.get_string('n'));
      IF mode_name='APLICAR' THEN
        ddl_text:='CREATE '||CASE WHEN item.get_string('unique')='UNIQUE' THEN 'UNIQUE ' END||'INDEX "'||item.get_string('n')||'" ON "'||item.get_string('table')||'" ("'||REPLACE(item.get_string('cols'),',','", "')||'")';
        BEGIN EXECUTE IMMEDIATE ddl_text;
        EXCEPTION WHEN OTHERS THEN DBMS_OUTPUT.PUT_LINE('FALLO INDEX '||item.get_string('n')||' SQLCODE='||SQLCODE); RAISE; END;
      END IF;
    ELSE DBMS_OUTPUT.PUT_LINE('CONSERVADO INDEX '||item.get_string('n')); END IF;
  END LOOP;
END;
/
'''

# Each SQL*Plus input line stays below its line limit; JSON may span whitespace boundaries.
manifest = json.dumps(tables, ensure_ascii=True, indent=2)
index_json = json.dumps(indexes, ensure_ascii=True, indent=2)
output = header.replace('__MANIFEST__', manifest).replace('__INDEXES__', index_json)
target = root / 'sql/09_reanudar_esquema_web_oracle.sql'
target.write_text(output, encoding='utf-8', newline='\n')
print(f'{target.name}: {len(tables)} tablas y {len(indexes)} indices; sin conexion a Oracle.')
