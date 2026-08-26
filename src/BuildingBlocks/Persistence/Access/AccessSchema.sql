-- =============================================================================================================
-- access schema — the executable form of visibility-matrix.md §2 and §3.
--
-- Everything here is idempotent: the platform migrator runs this file on every start so the predicate functions
-- can evolve without a hand-written migration per change. Policies that *use* these functions live in each
-- module's own EF migration (conventions.md §4), which is what keeps the matrix in one place while letting each
-- slice own its tables.
--
-- S0 ships the session accessors only. The per-entity predicates (can_read_activity, can_read_project, ...) need
-- business tables to reference and therefore land with S2.
-- =============================================================================================================

create schema if not exists access;

-- app_owner owns the schema and everything in it. Each module's migration adds its own predicates here — that is
-- what keeps the visibility matrix in one place instead of scattered across module schemas — and migrations run
-- as app_owner, so a superuser-owned schema would refuse every one of them with "permission denied for schema
-- access". Setting the role means the functions below are created owned by app_owner too, which is what lets a
-- later migration CREATE OR REPLACE them.
alter schema access owner to app_owner;

-- Reassign anything already in the schema. A database created before app_owner owned it holds functions owned by
-- the bootstrap superuser, and CREATE OR REPLACE below would fail with "must be owner of function uid" — so the
-- upgrade path from an earlier deploy breaks on the first startup rather than on the first request. Rerunning this
-- on an already-correct database is a no-op.
do $$
declare
    routine record;
begin
    for routine in
        select p.oid::regprocedure as signature
        from pg_proc p
        join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'access'
    loop
        execute format('alter function %s owner to app_owner', routine.signature);
    end loop;
end
$$;

set role app_owner;

-- -------------------------------------------------------------------------------------------------------------
-- Session accessors. Each reads one of the GUCs the RlsSessionInterceptor stamps on connection open.
-- `true` in current_setting means "return NULL if unset" rather than raising — an unstamped connection must
-- evaluate to no access, never to an error that a caller could mistake for a transient fault.
-- -------------------------------------------------------------------------------------------------------------

create or replace function access.uid() returns uuid
    language sql stable
    as $$ select nullif(current_setting('app.user_id', true), '')::uuid $$;

create or replace function access.unit() returns uuid
    language sql stable
    as $$ select nullif(current_setting('app.unit_id', true), '')::uuid $$;

create or replace function access.depts() returns uuid[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.dept_ids', true), ''), ',')::uuid[],
            '{}'::uuid[]
        )
    $$;

create or replace function access.roles() returns text[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.roles', true), ''), ','),
            '{}'::text[]
        )
    $$;

-- -------------------------------------------------------------------------------------------------------------
-- Node scope (v2 01 3). One path replaces every level-specific id: there is no app.bureau_id and no
-- app.service_id, and no predicate below names a level.
-- -------------------------------------------------------------------------------------------------------------

create or replace function access.node() returns uuid
    language sql stable
    as $$ select nullif(current_setting('app.node_id', true), '')::uuid $$;

create or replace function access.my_path() returns uuid[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.node_path', true), ''), ',')::uuid[],
            '{}'::uuid[]
        )
    $$;

create or replace function access.headed_nodes() returns uuid[]
    language sql stable
    as $$
        select coalesce(
            string_to_array(nullif(current_setting('app.headed_nodes', true), ''), ',')::uuid[],
            '{}'::uuid[]
        )
    $$;

-- The entire supervision rule. A head's node id appears in the ancestor_ids of every row beneath them at any
-- depth, so one array overlap answers "is this inside a subtree I head" without recursion and without level logic.
create or replace function access.in_my_subtree(p_ancestors uuid[]) returns boolean
    language sql stable
    as $$ select coalesce(p_ancestors && access.headed_nodes(), false) $$;

create or replace function access.same_node(p_node uuid) returns boolean
    language sql stable
    as $$ select p_node is not null and p_node = access.node() $$;

-- The node I hang off: the last entry of my path before myself.
create or replace function access.my_branch() returns uuid
    language sql stable
    as $$
        select case
            when array_length(path, 1) > 1 then path[array_length(path, 1) - 1]
        end
        from (select access.my_path() as path) as me
    $$;

create or replace function access.in_my_branch(p_ancestors uuid[]) returns boolean
    language sql stable
    as $$ select coalesce(access.my_branch() = any(p_ancestors), false) $$;

-- -------------------------------------------------------------------------------------------------------------
-- Role predicates.
-- -------------------------------------------------------------------------------------------------------------

create or replace function access.has(p_role text) returns boolean
    language sql stable
    as $$ select p_role = any(access.roles()) $$;

create or replace function access.is_head() returns boolean
    language sql stable
    as $$ select array_length(access.headed_nodes(), 1) > 0 or access.has('pmo') $$;

-- Background jobs (sync, AI, outbox drain) run under this role. Policies opt in to it explicitly; it is never a
-- blanket bypass, and the interceptor refuses to put it in a token-derived context.
create or replace function access.is_system() returns boolean
    language sql stable
    as $$ select access.has('system') $$;

-- True when the session has no scope at all. Useful in tests and in the /api/ping proof, and a readable way for a
-- policy to say "an unstamped connection sees nothing".
create or replace function access.is_scoped() returns boolean
    language sql stable
    as $$ select access.uid() is not null $$;

-- Roles whose reach does not stop at a branch. A head's authority does stop at one, so these are the roles a head
-- may never hand out (v2 08.1) -- granting a role that outranks the granter is escalation in its plainest form.
-- The list lives here, with the roles themselves, rather than in the policy that consults it: a second copy in a
-- module migration is a copy that stops being updated the day a role is added.
create or replace function access.outranks_a_branch(p_role text) returns boolean
    language sql immutable
    as $$ select p_role in ('pmo', 'admin', 'system') $$;

-- -------------------------------------------------------------------------------------------------------------
-- Node-tree plumbing (v2 01 3, 09 Ph.1-3). Lives here rather than in a module migration because every module's
-- migration calls it and module migrations run in registration order, which puts some of them before Directory's.
-- -------------------------------------------------------------------------------------------------------------

create table if not exists access.node_scoped_table (
    schema_name      text not null,
    table_name       text not null,
    node_column      text not null default 'node_id',
    ancestors_column text not null default 'node_ancestor_ids',
    primary key (schema_name, table_name)
);

create or replace function access.refresh_node_paths(p_node uuid) returns void
    language plpgsql as $fn$
declare
    scoped record;
begin
    for scoped in select * from access.node_scoped_table loop
        execute format(
            'update %I.%I s set %I = n.ancestor_ids
               from directory.org_node n
              where n.id = s.%I and s.%I = $1
                and s.%I is distinct from n.ancestor_ids',
            scoped.schema_name, scoped.table_name, scoped.ancestors_column,
            scoped.node_column, scoped.node_column, scoped.ancestors_column)
        using p_node;
    end loop;
end
$fn$;

create or replace function access.copy_node_path() returns trigger
    language plpgsql as $fn$
declare
    path uuid[];
    derived uuid;
    from_legacy boolean := false;
begin
    if new.node_id is null and tg_nargs > 0 then
        execute format(
            'select coalesce(%s)',
            (select string_agg(format('($1).%I', arg), ', ')
             from unnest(tg_argv) as arg))
        into derived using new;

        new.node_id := derived;
        from_legacy := true;
    end if;

    if new.node_id is null then
        new.node_ancestor_ids := '{}'::uuid[];
        return new;
    end if;

    select n.ancestor_ids into path from directory.org_node n where n.id = new.node_id;

    -- A node derived from the legacy columns may simply not be projected yet: during the shim a department can be
    -- written in the same transaction that will produce its node. An empty path matches no subtree, so the row is
    -- invisible rather than wrong, and the projection's sweep fills it in. A node_id somebody supplied explicitly
    -- gets no such benefit of the doubt.
    if path is null then
        if from_legacy then
            new.node_ancestor_ids := '{}'::uuid[];
            return new;
        end if;

        raise exception 'node % is not in the org tree', new.node_id
            using errcode = '23503';
    end if;

    new.node_ancestor_ids := path;
    return new;
end
$fn$;

create or replace function access.refresh_stale_node_paths() returns void
    language plpgsql as $fn$
declare
    scoped record;
begin
    for scoped in select * from access.node_scoped_table loop
        execute format(
            'update %I.%I s set %I = n.ancestor_ids
               from directory.org_node n
              where n.id = s.%I
                and (s.%I is null or s.%I = ''{}''::uuid[])',
            scoped.schema_name, scoped.table_name, scoped.ancestors_column,
            scoped.node_column, scoped.ancestors_column, scoped.ancestors_column);
    end loop;
end
$fn$;

create or replace function access.copy_person_node_path() returns trigger
    language plpgsql as $fn$
declare
    path uuid[];
begin
    select n.ancestor_ids into path from directory.org_node n where n.id = new.home_node_id;

    if path is null then
        raise exception 'node % is not in the org tree', new.home_node_id
            using errcode = '23503';
    end if;

    new.node_ancestor_ids := path;
    return new;
end
$fn$;

create or replace function access.attach_to_node_tree(
    p_schema text,
    p_table text,
    p_unit_col text,
    p_dept_col text,
    p_required boolean default true)
returns void language plpgsql as $fn$
declare
    source text;
    forced boolean;
begin
    -- The backfill below is an UPDATE, and several of the tables it runs against are append-only: they carry a
    -- read policy and an insert policy and deliberately no update policy at all. Under FORCE ROW LEVEL SECURITY
    -- that denies the backfill for every row — silently, because an UPDATE the policy refuses affects nothing and
    -- raises nothing. The migration then fails a hundred lines later on a not-null constraint, which is exactly
    -- how this surfaced: on a database with rows, never on the empty one the tests start from.
    --
    -- So force is lifted for the duration and restored to whatever it was. This is the owner backfilling its own
    -- columns during a migration, not a request: nothing here reads the session's identity, and every row is
    -- given the same treatment whoever is deploying.
    select relforcerowsecurity into forced
    from pg_class c
    join pg_namespace n on n.oid = c.relnamespace
    where n.nspname = p_schema and c.relname = p_table;

    if coalesce(forced, false) then
        execute format('alter table %I.%I no force row level security', p_schema, p_table);
    end if;

    execute format('alter table %I.%I add column if not exists node_id uuid', p_schema, p_table);
    execute format(
        'alter table %I.%I add column if not exists node_ancestor_ids uuid[] default ''{}''::uuid[]',
        p_schema, p_table);

    if p_unit_col is null then
        source := format('s.%I', p_dept_col);
    elsif p_dept_col is null then
        source := format('s.%I', p_unit_col);
    else
        source := format('coalesce(s.%I, s.%I)', p_unit_col, p_dept_col);
    end if;

    execute format(
        'update %I.%I s set node_id = n.id
           from directory.org_node n
          where n.id = %s and s.node_id is null',
        p_schema, p_table, source);

    execute format(
        'update %I.%I s set node_ancestor_ids = coalesce(n.ancestor_ids, ''{}''::uuid[])
           from directory.org_node n
          where n.id = s.node_id
            and s.node_ancestor_ids is distinct from n.ancestor_ids',
        p_schema, p_table);

    execute format(
        'update %I.%I set node_ancestor_ids = ''{}''::uuid[] where node_ancestor_ids is null',
        p_schema, p_table);

    if p_required then
        -- A row whose source column was never filled in has nowhere on the tree to hang, and the not-null below
        -- would refuse the whole migration rather than the row. The runbook's answer (v2 §09) is the placeholder
        -- branch: park it under A-RECLASSER, where it is visible and re-parentable, instead of blocking the
        -- deployment on data somebody entered before the column existed.
        --
        -- Only reachable on a database that already had rows. A fresh one has nothing to park.
        execute format(
            'update %I.%I set node_id = (select id from directory.org_node where code = %L)
              where node_id is null',
            p_schema, p_table, 'A-RECLASSER');

        execute format(
            'update %I.%I s set node_ancestor_ids = coalesce(n.ancestor_ids, ''{}''::uuid[])
               from directory.org_node n
              where n.id = s.node_id
                and s.node_ancestor_ids is distinct from n.ancestor_ids',
            p_schema, p_table);

        execute format('alter table %I.%I alter column node_id set not null', p_schema, p_table);
    end if;

    execute format(
        'alter table %I.%I alter column node_ancestor_ids set not null', p_schema, p_table);

    execute format(
        'create index if not exists %I on %I.%I using gin (node_ancestor_ids)',
        format('ix_%s_node_ancestor_ids', p_table), p_schema, p_table);

    execute format(
        'drop trigger if exists %I on %I.%I',
        format('%s_copy_node_path', p_table), p_schema, p_table);

    -- The trigger's column list is de-duplicated, because a table whose node column *is* its source column would
    -- otherwise name it twice and Postgres refuses a repeat. The argument list is NOT de-duplicated the same way:
    -- its order is the coalesce order, so the unit column has to stay ahead of the department one. Sorting them —
    -- which string_agg(distinct ...) quietly does — would derive every row's node from its department and undo
    -- the whole point of attaching at the narrowest level somebody actually sits at.
    execute format(
        'create trigger %I before insert or update of %s on %I.%I
             for each row execute function access.copy_node_path(%s)',
        format('%s_copy_node_path', p_table),
        (select string_agg(quote_ident(c), ', ')
         from (select distinct unnest(array_remove(array['node_id', p_unit_col, p_dept_col], null)) as c) as columns),
        p_schema,
        p_table,
        (select string_agg(quote_literal(c), ', ' order by ord)
         from unnest(array_remove(array[nullif(p_unit_col, 'node_id'), nullif(p_dept_col, 'node_id')], null))
             with ordinality as sources(c, ord)));

    insert into access.node_scoped_table (schema_name, table_name)
    values (p_schema, p_table)
    on conflict do nothing;

    if coalesce(forced, false) then
        execute format('alter table %I.%I force row level security', p_schema, p_table);
    end if;
end
$fn$;

create or replace function access.detach_from_node_tree(p_schema text, p_table text)
returns void language plpgsql as $fn$
begin
    execute format(
        'drop trigger if exists %I on %I.%I',
        format('%s_copy_node_path', p_table), p_schema, p_table);

    execute format('drop index if exists %I.%I', p_schema, format('ix_%s_node_ancestor_ids', p_table));

    execute format(
        'alter table %I.%I drop column if exists node_ancestor_ids, drop column if exists node_id',
        p_schema, p_table);

    delete from access.node_scoped_table
        where schema_name = p_schema and table_name = p_table;
end
$fn$;

grant select on access.node_scoped_table to app_rw;

-- -------------------------------------------------------------------------------------------------------------
-- Grants. app_rw executes the predicates but owns nothing, so FORCE ROW LEVEL SECURITY applies to it.
-- -------------------------------------------------------------------------------------------------------------

grant usage on schema access to app_rw;
grant execute on all functions in schema access to app_rw;

alter default privileges in schema access grant execute on functions to app_rw;

reset role;
