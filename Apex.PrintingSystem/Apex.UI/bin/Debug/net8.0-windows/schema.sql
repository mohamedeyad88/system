-- Database Schema for Apex Printing System

-- 1. Clients & Schools
CREATE TABLE Clients (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(200) NOT NULL,
    Type NVARCHAR(50) NOT NULL, -- 'Single', 'School'
    ContactInfo NVARCHAR(MAX),
    CreatedAt DATETIME DEFAULT GETDATE()
);

CREATE TABLE Schools (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ClientId INT NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    FOREIGN KEY (ClientId) REFERENCES Clients(Id) ON DELETE CASCADE
);

CREATE TABLE Categories (
    Id INT PRIMARY KEY IDENTITY(1,1),
    SchoolId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL, -- 'Primary', 'Preparatory', 'Secondary'
    FOREIGN KEY (SchoolId) REFERENCES Schools(Id) ON DELETE CASCADE
);

CREATE TABLE Grades (
    Id INT PRIMARY KEY IDENTITY(1,1),
    CategoryId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
);

CREATE TABLE Subjects (
    Id INT PRIMARY KEY IDENTITY(1,1),
    GradeId INT NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Pages INT NOT NULL DEFAULT 0,
    CoverPrice DECIMAL(18, 2) NOT NULL DEFAULT 0,
    PagePrice DECIMAL(18, 2) NOT NULL DEFAULT 0,
    IsShared BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (GradeId) REFERENCES Grades(Id) ON DELETE CASCADE
);

-- 2. Orders & Invoices
CREATE TABLE Orders (
    Id INT PRIMARY KEY IDENTITY(1,1),
    ClientId INT NOT NULL,
    OrderDate DATETIME DEFAULT GETDATE(),
    TotalAmount DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending', -- 'Pending', 'Completed', 'Paid'
    FOREIGN KEY (ClientId) REFERENCES Clients(Id)
);

CREATE TABLE OrderItems (
    Id INT PRIMARY KEY IDENTITY(1,1),
    OrderId INT NOT NULL,
    SubjectId INT NOT NULL,
    Copies INT NOT NULL DEFAULT 1,
    SubTotal DECIMAL(18, 2) NOT NULL DEFAULT 0,
    FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
    FOREIGN KEY (SubjectId) REFERENCES Subjects(Id)
);

-- 3. Expenses & Employees
CREATE TABLE Employees (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(200) NOT NULL,
    Role NVARCHAR(100),
    BaseSalary DECIMAL(18, 2) NOT NULL DEFAULT 0,
    IsActive BIT DEFAULT 1
);

CREATE TABLE ExpenseCategories (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE Expenses (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Date DATETIME DEFAULT GETDATE(),
    Amount DECIMAL(18, 2) NOT NULL,
    Description NVARCHAR(MAX),
    CategoryId INT NOT NULL,
    EmployeeId INT NULL, -- Nullable, only for salary payments
    FOREIGN KEY (CategoryId) REFERENCES ExpenseCategories(Id),
    FOREIGN KEY (EmployeeId) REFERENCES Employees(Id)
);

-- 4. Attendance
CREATE TABLE AttendanceRecords (
    Id INT PRIMARY KEY IDENTITY(1,1),
    EmployeeId INT NOT NULL,
    Date DATE NOT NULL,
    CheckInTime TIME NULL,
    CheckOutTime TIME NULL,
    Status NVARCHAR(50) NOT NULL, -- 'Present', 'Absent', 'Late'
    FOREIGN KEY (EmployeeId) REFERENCES Employees(Id)
);

-- 5. Printing Operations
CREATE TABLE Printers (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Name NVARCHAR(200) NOT NULL,
    IpAddress NVARCHAR(50) NOT NULL,
    Port INT DEFAULT 9100,
    Status NVARCHAR(50) DEFAULT 'Offline' -- 'Online', 'Offline', 'Printing', 'Error'
);

CREATE TABLE PrintJobs (
    Id INT PRIMARY KEY IDENTITY(1,1),
    FileName NVARCHAR(MAX) NOT NULL,
    TotalCopies INT NOT NULL,
    PrintedCopies INT DEFAULT 0,
    Status NVARCHAR(50) DEFAULT 'Queued', -- 'Queued', 'Printing', 'Completed', 'Paused'
    CreatedAt DATETIME DEFAULT GETDATE()
);

CREATE TABLE JobAssignments (
    Id INT PRIMARY KEY IDENTITY(1,1),
    PrintJobId INT NOT NULL,
    PrinterId INT NOT NULL,
    AssignedCopies INT NOT NULL,
    CompletedCopies INT DEFAULT 0,
    Status NVARCHAR(50) DEFAULT 'Pending', -- 'Pending', 'InProgress', 'Done', 'Failed'
    FOREIGN KEY (PrintJobId) REFERENCES PrintJobs(Id) ON DELETE CASCADE,
    FOREIGN KEY (PrinterId) REFERENCES Printers(Id)
);

-- Seed Data
INSERT INTO ExpenseCategories (Name) VALUES ('Materials'), ('Transport'), ('Salary'), ('Maintenance'), ('Other');
