CREATE TABLE IF NOT EXISTS Projects (
    ProjectId TEXT PRIMARY KEY NOT NULL,
    Name TEXT NOT NULL,
    BuildingArea TEXT NULL,
    TaxRate TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS FixedWorkItems (
    Id TEXT PRIMARY KEY NOT NULL,
    ProjectId TEXT NOT NULL REFERENCES Projects(ProjectId),
    SortOrder INTEGER NOT NULL CHECK (SortOrder BETWEEN 1 AND 6),
    Name TEXT NOT NULL,
    IncomeCents INTEGER NOT NULL,
    CostCents INTEGER NOT NULL CHECK (CostCents >= 0),
    UNIQUE(ProjectId, SortOrder)
);
CREATE TABLE IF NOT EXISTS DesignFees (
    ProjectId TEXT PRIMARY KEY NOT NULL REFERENCES Projects(ProjectId),
    IncomeCents INTEGER NOT NULL,
    CostCents INTEGER NOT NULL CHECK (CostCents >= 0)
);
CREATE TABLE IF NOT EXISTS OtherFees (
    Id TEXT PRIMARY KEY NOT NULL,
    ProjectId TEXT NOT NULL REFERENCES Projects(ProjectId),
    Name TEXT NOT NULL CHECK (length(trim(Name)) > 0),
    NormalizedName TEXT NOT NULL,
    IncomeCents INTEGER NULL,
    CostCents INTEGER NOT NULL CHECK (CostCents >= 0),
    Remark TEXT NULL,
    UNIQUE(ProjectId, NormalizedName)
);
