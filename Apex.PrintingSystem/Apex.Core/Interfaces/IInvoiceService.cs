using Apex.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apex.Core.Interfaces
{
    /// <summary>
    /// Interface for invoice management operations.
    /// </summary>
    public interface IInvoiceService
    {
        /// <summary>
        /// Creates a new invoice for a print job.
        /// </summary>
        Task<Invoice> CreateInvoiceAsync(int printJobId, string customerName, string customerContact);

        /// <summary>
        /// Gets an invoice by ID.
        /// </summary>
        Task<Invoice?> GetInvoiceByIdAsync(int invoiceId);

        /// <summary>
        /// Gets all invoices.
        /// </summary>
        Task<IEnumerable<Invoice>> GetAllInvoicesAsync();

        /// <summary>
        /// Gets invoices by status.
        /// </summary>
        Task<IEnumerable<Invoice>> GetInvoicesByStatusAsync(InvoiceStatus status);

        /// <summary>
        /// Updates an invoice.
        /// </summary>
        Task<bool> UpdateInvoiceAsync(Invoice invoice);

        /// <summary>
        /// Marks an invoice as paid.
        /// </summary>
        Task<bool> MarkAsPaidAsync(int invoiceId, decimal amountPaid, string paymentMethod);

        /// <summary>
        /// Deletes an invoice.
        /// </summary>
        Task<bool> DeleteInvoiceAsync(int invoiceId);
    }
}
