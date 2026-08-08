using Apex.Core.Models;
using Apex.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apex.Services
{
    /// <summary>
    /// Service for managing invoices and billing operations.
    /// </summary>
    public class InvoiceService : IInvoiceService
    {
        private readonly IRepository<Invoice> _invoiceRepository;
        private readonly IRepository<PrintJob> _printJobRepository;
        private readonly ILoggerService _logger;

        public InvoiceService(
            IRepository<Invoice> invoiceRepository,
            IRepository<PrintJob> printJobRepository,
            ILoggerService logger)
        {
            _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _printJobRepository = printJobRepository ?? throw new ArgumentNullException(nameof(printJobRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a new invoice for a print job.
        /// </summary>
        public async Task<Invoice> CreateInvoiceAsync(int printJobId, string customerName, string customerContact)
        {
            try
            {
                if (printJobId <= 0)
                    throw new ArgumentException("Invalid print job ID", nameof(printJobId));

                if (string.IsNullOrWhiteSpace(customerName))
                    throw new ArgumentException("Customer name is required", nameof(customerName));

                var printJob = await _printJobRepository.GetByIdAsync(printJobId);
                if (printJob == null)
                    throw new InvalidOperationException($"Print job with ID {printJobId} not found");

                var invoice = new Invoice
                {
                    InvoiceNumber = GenerateInvoiceNumber(),
                    PrintJobId = printJobId,
                    CustomerName = customerName,
                    CustomerContact = customerContact,
                    InvoiceDate = DateTime.Now,
                    DueDate = DateTime.Now.AddDays(30),
                    TotalAmount = CalculateTotalAmount(printJob),
                    Status = InvoiceStatus.Pending,
                    CreatedAt = DateTime.Now
                };

                await _invoiceRepository.AddAsync(invoice);
                _logger.Log(LogLevel.Info, $"Invoice {invoice.InvoiceNumber} created for print job {printJobId}");

                return invoice;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error creating invoice for print job {printJobId}", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Gets an invoice by ID.
        /// </summary>
        public async Task<Invoice?> GetInvoiceByIdAsync(int invoiceId)
        {
            try
            {
                if (invoiceId <= 0)
                    throw new ArgumentException("Invalid invoice ID", nameof(invoiceId));

                return await _invoiceRepository.GetByIdAsync(invoiceId);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error retrieving invoice {invoiceId}", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Gets all invoices.
        /// </summary>
        public async Task<IEnumerable<Invoice>> GetAllInvoicesAsync()
        {
            try
            {
                return await _invoiceRepository.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error retrieving all invoices", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Gets invoices by status.
        /// </summary>
        public async Task<IEnumerable<Invoice>> GetInvoicesByStatusAsync(InvoiceStatus status)
        {
            try
            {
                var allInvoices = await _invoiceRepository.GetAllAsync();
                return allInvoices.Where(i => i.Status == status);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error retrieving invoices by status {status}", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Updates an invoice.
        /// </summary>
        public async Task<bool> UpdateInvoiceAsync(Invoice invoice)
        {
            try
            {
                if (invoice == null)
                    throw new ArgumentNullException(nameof(invoice));

                invoice.UpdatedAt = DateTime.Now;
                await _invoiceRepository.UpdateAsync(invoice);
                _logger.Log(LogLevel.Info, $"Invoice {invoice.InvoiceNumber} updated");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error updating invoice {invoice?.InvoiceNumber}", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Marks an invoice as paid.
        /// </summary>
        public async Task<bool> MarkAsPaidAsync(int invoiceId, decimal amountPaid, string paymentMethod)
        {
            try
            {
                if (invoiceId <= 0)
                    throw new ArgumentException("Invalid invoice ID", nameof(invoiceId));

                if (amountPaid <= 0)
                    throw new ArgumentException("Amount paid must be positive", nameof(amountPaid));

                var invoice = await _invoiceRepository.GetByIdAsync(invoiceId);
                if (invoice == null)
                    throw new InvalidOperationException($"Invoice with ID {invoiceId} not found");

                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAmount = amountPaid;
                invoice.PaymentMethod = paymentMethod;
                invoice.PaymentDate = DateTime.Now;
                invoice.UpdatedAt = DateTime.Now;

                await _invoiceRepository.UpdateAsync(invoice);
                _logger.Log(LogLevel.Info, $"Invoice {invoice.InvoiceNumber} marked as paid");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error marking invoice {invoiceId} as paid", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Deletes an invoice.
        /// </summary>
        public async Task<bool> DeleteInvoiceAsync(int invoiceId)
        {
            try
            {
                if (invoiceId <= 0)
                    throw new ArgumentException("Invalid invoice ID", nameof(invoiceId));

                var invoice = await _invoiceRepository.GetByIdAsync(invoiceId);
                if (invoice == null)
                    return false;

                await _invoiceRepository.DeleteAsync(invoice);
                _logger.Log(LogLevel.Info, $"Invoice {invoice.InvoiceNumber} deleted");

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, $"Error deleting invoice {invoiceId}", ex: ex);
                throw;
            }
        }

        /// <summary>
        /// Generates a unique invoice number.
        /// </summary>
        private string GenerateInvoiceNumber()
        {
            return $"INV-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
        }

        /// <summary>
        /// Calculates the total amount for a print job.
        /// </summary>
        private decimal CalculateTotalAmount(PrintJob printJob)
        {
            if (printJob == null)
                return 0;

            // Basic calculation - can be enhanced based on business rules
            decimal baseAmount = printJob.TotalPages * 0.5m; // 0.5 EGP per page
            decimal quantity = printJob.TotalCopies;

            return baseAmount * quantity;
        }
    }
}
