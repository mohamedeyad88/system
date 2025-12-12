using System;

namespace Apex.Core.Models
{
    /// <summary>
    /// Represents an invoice for a print job.
    /// </summary>
    public class Invoice
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Unique invoice number (e.g., INV-20240101-ABC123).
        /// </summary>
        public string InvoiceNumber { get; set; } = string.Empty;
        
        /// <summary>
        /// Associated print job ID.
        /// </summary>
        public int PrintJobId { get; set; }
        
        /// <summary>
        /// Customer name.
        /// </summary>
        public string CustomerName { get; set; } = string.Empty;
        
        /// <summary>
        /// Customer contact information (phone/email).
        /// </summary>
        public string? CustomerContact { get; set; }
        
        /// <summary>
        /// Invoice creation date.
        /// </summary>
        public DateTime InvoiceDate { get; set; }
        
        /// <summary>
        /// Payment due date.
        /// </summary>
        public DateTime DueDate { get; set; }
        
        /// <summary>
        /// Total invoice amount.
        /// </summary>
        public decimal TotalAmount { get; set; }
        
        /// <summary>
        /// Amount paid (if any).
        /// </summary>
        public decimal PaidAmount { get; set; }
        
        /// <summary>
        /// Payment method (Cash, Card, Transfer, etc.).
        /// </summary>
        public string? PaymentMethod { get; set; }
        
        /// <summary>
        /// Payment date (if paid).
        /// </summary>
        public DateTime? PaymentDate { get; set; }
        
        /// <summary>
        /// Invoice status.
        /// </summary>
        public InvoiceStatus Status { get; set; }
        
        /// <summary>
        /// Additional notes or comments.
        /// </summary>
        public string? Notes { get; set; }
        
        /// <summary>
        /// Creation timestamp.
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// Navigation property to the associated print job.
        /// </summary>
        public PrintJob? PrintJob { get; set; }
        
        /// <summary>
        /// Remaining balance to be paid.
        /// </summary>
        public decimal Balance => TotalAmount - PaidAmount;
        
        /// <summary>
        /// Whether the invoice is fully paid.
        /// </summary>
        public bool IsFullyPaid => PaidAmount >= TotalAmount;
        
        /// <summary>
        /// Whether the invoice is overdue.
        /// </summary>
        public bool IsOverdue => Status == InvoiceStatus.Pending && DateTime.Now > DueDate;
    }
    
    /// <summary>
    /// Invoice status enumeration.
    /// </summary>
    public enum InvoiceStatus
    {
        /// <summary>
        /// Invoice is pending payment.
        /// </summary>
        Pending = 0,
        
        /// <summary>
        /// Invoice has been paid in full.
        /// </summary>
        Paid = 1,
        
        /// <summary>
        /// Invoice is overdue.
        /// </summary>
        Overdue = 2,
        
        /// <summary>
        /// Invoice has been cancelled.
        /// </summary>
        Cancelled = 3,
        
        /// <summary>
        /// Invoice is partially paid.
        /// </summary>
        PartiallyPaid = 4
    }
}
