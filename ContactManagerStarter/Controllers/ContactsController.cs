using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ContactManager.Data;
using ContactManager.Hubs;
using ContactManager.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MailKit;
using MimeKit;
using MailKit.Net.Smtp;

namespace ContactManager.Controllers
{
    public class ContactsController : Controller
    {
        private readonly ApplicationContext _context;
        private readonly IHubContext<ContactHub> _hubContext;
        private readonly ILogger<ContactsController> _logger;

        public ContactsController(ApplicationContext context, IHubContext<ContactHub> hubContext, ILogger<ContactsController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;

        }

        public async Task<IActionResult> DeleteContact(Guid id)
        {
            try
            {
                var contactToDelete = await _context.Contacts
                .Include(x => x.EmailAddresses)
                .FirstOrDefaultAsync(x => x.Id == id);

                if (contactToDelete == null)
                {
                    return BadRequest();
                }

                _context.EmailAddresses.RemoveRange(contactToDelete.EmailAddresses);
                _context.Contacts.Remove(contactToDelete);

                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("Update");

                _logger.LogInformation("Deleting contact info successfully");
                return Ok("Deleting contact info successfully");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error deleting contact info in the database");
                return StatusCode(500, "Error deleting contact info in the database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while deleting contact info");
                return StatusCode(500, "Unexpected error occurred");
            }
        }

        public async Task<IActionResult> EditContact(Guid id)
        {
            try
            {
                var contact = await _context.Contacts
                .Include(x => x.EmailAddresses)
                .Include(x => x.Addresses)
                .FirstOrDefaultAsync(x => x.Id == id);

                if (contact == null)
                {
                    return NotFound();
                }

                var viewModel = new EditContactViewModel
                {
                    Id = contact.Id,
                    Title = contact.Title,
                    FirstName = contact.FirstName,
                    LastName = contact.LastName,
                    DOB = contact.DOB,
                    EmailAddresses = contact.EmailAddresses,
                    Addresses = contact.Addresses
                };
                _logger.LogInformation("Contact edited");
                return PartialView("_EditContact", viewModel);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error retrieving contact info in the database");
                return StatusCode(500, "Error retriving contact info in the database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while editing contact info");
                return StatusCode(500, "Unexpected error occurred");
            }

        }

        public async Task<IActionResult> GetContacts()
        {
            try
            {
                var contactList = await _context.Contacts
                    .OrderBy(x => x.FirstName)
                    .ToListAsync();
                var emailAddresses = await _context.EmailAddresses.ToListAsync();
                return PartialView("_ContactTable", new ContactViewModel { Contacts = contactList, EmailAddresses = emailAddresses });
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error retrieving contact list and email address");
                return StatusCode(500, "Error retrieving contact list and email address");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Contact view was not found");
                return StatusCode(404, "Contact view was not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpecting error occurred while loading contacts");
                return StatusCode(500, "Unexpected error occurred");
            }
        }

        public IActionResult Index()
            {
                return View();
            }

        public IActionResult NewContact()
        {
            return PartialView("_EditContact", new EditContactViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> SaveContact([FromBody]SaveContactViewModel model)
        {
            try
            {
                var contact = model.ContactId == Guid.Empty
                ? new Contact { Title = model.Title, FirstName = model.FirstName, LastName = model.LastName, DOB = model.DOB }
                : await _context.Contacts.Include(x => x.EmailAddresses).Include(x => x.Addresses).FirstOrDefaultAsync(x => x.Id == model.ContactId);

                if (contact == null)
                {
                    return NotFound("Contact not found");
                }

                _context.EmailAddresses.RemoveRange(contact.EmailAddresses);
                _context.Addresses.RemoveRange(contact.Addresses);
                foreach (var email in model.Emails)
                {
                    contact.EmailAddresses.Add(new EmailAddress
                    {
                        Type = email.Type,
                        Email = email.Email,
                        Contact = contact
                    });
                }

                foreach (var address in model.Addresses)
                {
                    contact.Addresses.Add(new Address
                    {
                        Street1 = address.Street1,
                        Street2 = address.Street2,
                        City = address.City,
                        State = address.State,
                        Zip = address.Zip,
                        Type = address.Type
                    });
                }
                contact.Title = model.Title;
                contact.FirstName = model.FirstName;
                contact.LastName = model.LastName;
                contact.DOB = model.DOB;

                if (model.ContactId == Guid.Empty)
                {
                    await _context.Contacts.AddAsync(contact);
                }
                else
                {
                    _context.Contacts.Update(contact);
                }

                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("Update");

                //SendEmailNotification(contact.Id);
                _logger.LogInformation("Contact saved successfully");
                return Ok("Contact saved successfully");
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Error saving contact info in the database");
                return StatusCode(500, "Error saving contact info in the database");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while saving contact info");
                return StatusCode(500, "Unexpected error occurred");
            }
            finally
            {
                await GetContacts();
            }
        }
    }

}