namespace Cracra.Modules.Directory.Services;

public static class OrgTreeSql
{
    public static readonly Guid UnclassifiedRootId = Guid.Parse("d1000000-0000-0000-0000-000000000001");

    public const string UnclassifiedRootCode = "A-RECLASSER";

    public const string Project = """
        do $proj$
        declare
            root constant uuid := 'd1000000-0000-0000-0000-000000000001';
            lvl int;
        begin
            insert into directory.org_node
                (id, parent_id, level_no, code, name, active, created_at, modified_at)
            values (root, null, 1, 'A-RECLASSER', 'À reclasser', true, now(), now())
            on conflict (id) do nothing;

            for lvl in 2..8 loop
                insert into directory.org_node
                    (id, parent_id, level_no, code, name, profile_id, active, created_at, modified_at)
                select d.id,
                       coalesce(d.parent_department_id, root),
                       lvl,
                       d.code,
                       d.name_key,
                       d.profile_id,
                       d.active,
                       d.created_at,
                       now()
                from directory.department d
                where case
                          when d.parent_department_id is null then 2
                          else coalesce(
                              (select p.level_no + 1 from directory.org_node p
                               where p.id = d.parent_department_id), 0)
                      end = lvl
                  and not exists (select 1 from directory.org_node n where n.id = d.id);
            end loop;

            insert into directory.org_node
                (id, parent_id, level_no, code, name, profile_id, active, created_at, modified_at)
            select u.id,
                   u.department_id,
                   p.level_no + 1,
                   u.code,
                   u.name,
                   u.profile_id,
                   u.active,
                   u.created_at,
                   now()
            from directory.unit u
            join directory.org_node p on p.id = u.department_id
            where not exists (select 1 from directory.org_node n where n.id = u.id);

            update directory.org_node n
               set code = d.code,
                   name = d.name_key,
                   profile_id = d.profile_id,
                   active = d.active,
                   modified_at = now()
            from directory.department d
            where d.id = n.id
              and (n.code, n.name, n.profile_id, n.active)
                  is distinct from (d.code, d.name_key, d.profile_id, d.active);

            update directory.org_node n
               set code = u.code,
                   name = u.name,
                   profile_id = u.profile_id,
                   active = u.active,
                   modified_at = now()
            from directory.unit u
            where u.id = n.id
              and (n.code, n.name, n.profile_id, n.active)
                  is distinct from (u.code, u.name, u.profile_id, u.active);

            update directory.org_node n
               set parent_id = u.department_id
            from directory.unit u
            where u.id = n.id
              and n.parent_id is distinct from u.department_id;

            perform access.refresh_stale_node_paths();
        end
        $proj$;
        """;
}
