using ASNLawReferenceAPI.Models;
using ASNLawReferenceAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ASNLawReferenceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AnnotationsController : ControllerBase
    {
        private readonly IAnnotationService _annotationService;
        private readonly ILogger<AnnotationsController> _logger;

        public AnnotationsController(
            IAnnotationService annotationService,
            ILogger<AnnotationsController> logger)
        {
            _annotationService = annotationService;
            _logger = logger;
        }

        [HttpGet("document/{documentId}")]
        public async Task<ActionResult<IEnumerable<AnnotationDto>>> GetByDocument(Guid documentId)
        {
            try
            {
                var annotations = await _annotationService.GetByDocumentIdAsync(documentId, User);
                return Ok(annotations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving annotations for document {DocumentId}", documentId);
                return StatusCode(500, "An error occurred while retrieving annotations");
            }
        }

        [HttpGet("chunk/{chunkId}")]
        public async Task<ActionResult<IEnumerable<AnnotationDto>>> GetByChunk(Guid chunkId)
        {
            try
            {
                var annotations = await _annotationService.GetByChunkIdAsync(chunkId, User);
                return Ok(annotations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving annotations for chunk {ChunkId}", chunkId);
                return StatusCode(500, "An error occurred while retrieving annotations");
            }
        }

        [HttpGet("thread/{annotationId}")]
        public async Task<ActionResult<IEnumerable<AnnotationDto>>> GetThread(Guid annotationId)
        {
            try
            {
                var thread = await _annotationService.GetThreadAsync(annotationId, User);
                return Ok(thread);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving thread for annotation {AnnotationId}", annotationId);
                return StatusCode(500, "An error occurred while retrieving the thread");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<AnnotationDto>> GetById(Guid id)
        {
            try
            {
                var annotation = await _annotationService.GetByIdAsync(id, User);
                if (annotation == null)
                    return NotFound();

                return Ok(annotation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving annotation {AnnotationId}", id);
                return StatusCode(500, "An error occurred while retrieving the annotation");
            }
        }

        [HttpGet("user")]
        public async Task<ActionResult<IEnumerable<AnnotationDto>>> GetUserAnnotations()
        {
            try
            {
                var annotations = await _annotationService.GetUserAnnotationsAsync(User);
                return Ok(annotations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user annotations");
                return StatusCode(500, "An error occurred while retrieving user annotations");
            }
        }

        [HttpPost]
        public async Task<ActionResult<AnnotationDto>> Create(CreateAnnotationDto createAnnotationDto)
        {
            try
            {
                var annotation = await _annotationService.CreateAsync(createAnnotationDto, User);
                return CreatedAtAction(nameof(GetById), new { id = annotation.Id }, annotation);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating annotation");
                return StatusCode(500, "An error occurred while creating the annotation");
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<AnnotationDto>> Update(Guid id, CreateAnnotationDto updateAnnotationDto)
        {
            try
            {
                var annotation = await _annotationService.UpdateAsync(id, updateAnnotationDto, User);
                if (annotation == null)
                    return NotFound();

                return Ok(annotation);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating annotation {AnnotationId}", id);
                return StatusCode(500, "An error occurred while updating the annotation");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                var result = await _annotationService.DeleteAsync(id, User);
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
                _logger.LogError(ex, "Error deleting annotation {AnnotationId}", id);
                return StatusCode(500, "An error occurred while deleting the annotation");
            }
        }

        [HttpPost("{id}/approve")]
        [Authorize(Policy = "ResearcherPolicy")]
        public async Task<IActionResult> Approve(Guid id)
        {
            try
            {
                var result = await _annotationService.ApproveAsync(id, User);
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
                _logger.LogError(ex, "Error approving annotation {AnnotationId}", id);
                return StatusCode(500, "An error occurred while approving the annotation");
            }
        }
    }
}