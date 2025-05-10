using LegalReferenceAPI.Models;
using LegalReferenceAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalReferenceAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _searchService;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            ISearchService searchService,
            ILogger<SearchController> logger)
        {
            _searchService = searchService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<ActionResult<IEnumerable<SearchResultDto>>> Search(SearchQueryDto searchQuery)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchQuery.Query))
                    return BadRequest("Search query cannot be empty");

                var results = await _searchService.SearchAsync(searchQuery, User);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing search for query: {Query}", searchQuery.Query);
                return StatusCode(500, "An error occurred while searching");
            }
        }

        [HttpGet("recommend/{documentId}")]
        public async Task<ActionResult<IEnumerable<SearchResultDto>>> GetRecommendations(Guid documentId, [FromQuery] int count = 5)
        {
            try
            {
                var results = await _searchService.GetRecommendedDocumentsAsync(documentId, count, User);
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recommendations for document {DocumentId}", documentId);
                return StatusCode(500, "An error occurred while getting recommendations");
            }
        }
    }
}