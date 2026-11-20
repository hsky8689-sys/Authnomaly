using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <summary>
    /// The insert functions called from the repositories (SqlQuery) used to be created by hand,
    /// so every database built only from migrations (container, test DB, Testcontainers) lacked them.
    /// </summary>
    public partial class DatabaseFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.add_user(username character varying, email character varying)
                 RETURNS numeric
                 LANGUAGE plpgsql
                AS $function$
                declare
                    Value numeric := 0;
                begin
                    INSERT INTO "Users"("Username","Email")
                    VALUES (username,email)
                    RETURNING "Id" INTO Value;
                    return Value;
                end;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.add_user_credentials(p_id bigint, p_password_hash text, p_salt bytea)
                 RETURNS numeric
                 LANGUAGE plpgsql
                AS $function$
                declare
                    Value numeric := 0;
                begin
                    INSERT INTO "AuthCredentials"("Id","PasswordHash","Salt")
                    VALUES (p_id,p_password_hash,p_salt)
                    RETURNING "Id" INTO Value;
                    return Value;
                end;
                $function$;
                """);

            // Old overloads from before the Strategy column; with them still present a call could resolve to the wrong one
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.add_endpoint_ratelimit_strategy(text, text, text, integer, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.add_endpoint_ratelimit_strategy(bigint, text, text, text, integer, integer, integer);");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION public.add_endpoint_ratelimit_strategy(p_path text, p_method text, p_field_name text, p_order integer, p_window_seconds integer, p_limit integer, p_strategy integer)
                 RETURNS numeric
                 LANGUAGE plpgsql
                AS $function$
                declare
                    Value numeric := 0;
                begin
                    INSERT INTO "EndpointsRateLimitStats"("Path","Method","FieldName","Order","WindowSeconds","Limit","Strategy")
                    VALUES (p_path,p_method,p_field_name,p_order,p_window_seconds,p_limit,p_strategy)
                    RETURNING "Id" INTO Value;
                    return Value;
                end;
                $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.add_endpoint_ratelimit_strategy(text, text, text, integer, integer, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.add_user_credentials(bigint, text, bytea);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.add_user(character varying, character varying);");
        }
    }
}
