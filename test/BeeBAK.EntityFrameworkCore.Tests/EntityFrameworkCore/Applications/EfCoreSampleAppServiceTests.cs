using BeeBAK.Samples;
using Xunit;

namespace BeeBAK.EntityFrameworkCore.Applications;

[Collection(BeeBAKTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<BeeBAKEntityFrameworkCoreTestModule>
{

}
