-- Create history_service_db if not exists
SELECT 'CREATE DATABASE history_service_db'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'history_service_db')\gexec

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE history_service_db TO auth_user;
