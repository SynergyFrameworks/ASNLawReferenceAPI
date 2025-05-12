using ASNLawReferenceAPI.Models;
using ASNLawReferenceAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ASNLawReferenceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentsController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(
            IDocumentService documentService,
            ILogger<DocumentsController> logger)
        {
            _documentService = documentService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DocumentDto>>> GetAll()
        {
            try
            {
                var documents = await _documentService.GetAllAsync();
                return Ok(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all documents");
                return StatusCode(500, "An error occurred while retrieving documents");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<DocumentDto>> GetById(Guid id)
        {
            try
            {
                var document = await _documentService.GetByIdAsync(id);
                if (document == null)
                    return NotFound();

                // Log access
                await _documentService.LogAccessAsync(id, "View", User);

                return Ok(document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving document {DocumentId}", id);
                return StatusCode(500, "An error occurred while retrieving the document");
            }
        }

        [HttpGet("jurisdiction/{jurisdiction}")]
        public async Task<ActionResult<IEnumerable<DocumentDto>>> GetByJurisdiction(string jurisdiction)
        {
            try
            {
                var documents = await _documentService.GetByJurisdictionAsync(jurisdiction);
                return Ok(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving documents for jurisdiction {Jurisdiction}", jurisdiction);
                return StatusCode(500, "An error occurred while retrieving documents");
            }
        }

        [HttpGet("{id}/versions")]
        public async Task<ActionResult<IEnumerable<DocumentDto>>> GetVersionHistory(Guid id)
        {
            try
            {
                var documents = await _documentService.GetVersionHistoryAsync(id);
                return Ok(documents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving version history for document {DocumentId}", id);
                return StatusCode(500, "An error occurred while retrieving version history");
            }
        }

        [HttpPost]
        [Authorize(Policy = "ResearcherPolicy")]
        public async Task<ActionResult<DocumentDto>> Upload([FromForm] CreateDocumentDto createDocumentDto)
        {
            try
            {
                if (createDocumentDto.PdfFile == null || createDocumentDto.PdfFile.Length == 0)
                    return BadRequest("No file uploaded");

                if (!createDocumentDto.PdfFile.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("File must be a PDF");

                var document = await _documentService.UploadAsync(createDocumentDto, User);
                return CreatedAtAction(nameof(GetById), new { id = document.Id }, document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document");
                return StatusCode(500, "An error occurred while uploading the document");
            }
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "ResearcherPolicy")]
        public async Task<ActionResult<DocumentDto>> Update(Guid id, [FromForm] CreateDocumentDto updateDocumentDto)
        {
            try
            {
                if (updateDocumentDto.PdfFile != null && !updateDocumentDto.PdfFile.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                    return BadRequest("File must be a PDF");

                var document = await _documentService.UpdateAsync(id, updateDocumentDto, User);
                if (document == null)
                    return NotFound();

                return Ok(document);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating document {DocumentId}", id);
                return StatusCode(500, "An error occurred while updating the document");
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminPolicy")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                var result = await _documentService.DeleteAsync(id, User);
                if (!result)
                    return NotFound();

                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document {DocumentId}", id);
                return StatusCode(500, "An error occurred while deleting the document");
            }
        }
    }
}