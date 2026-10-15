// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.messages

import android.os.Bundle
import android.text.Html
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.LinearLayoutManager
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.data.api.dto.InboxMessage
import za.co.theebuhlefoundation.databinding.FragmentListBinding
import za.co.theebuhlefoundation.databinding.ItemMessageBinding
import za.co.theebuhlefoundation.ui.common.BindingAdapter
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.Format
import za.co.theebuhlefoundation.ui.common.StateViews
import za.co.theebuhlefoundation.ui.common.container

class MessagesFragment : Fragment() {

    private var _binding: FragmentListBinding? = null
    private val binding get() = _binding!!

    private lateinit var state: StateViews

    private val viewModel: MessagesViewModel by viewModels {
        ContainerViewModelFactory(container) { MessagesViewModel(it.messages) }
    }

    private val adapter = BindingAdapter<InboxMessage, ItemMessageBinding>(
        inflate = ItemMessageBinding::inflate,
        identity = { it.announcementId },
    ) { binding, message, _ ->
        binding.messageCategory.text = message.category.spacedUpper()
        binding.messageTitle.text = message.title

        // The body was sanitised on write against an eleven-tag allow-list, so
        // what arrives is already safe to render. FROM_HTML_MODE_COMPACT keeps
        // the paragraphs a staff member typed without the double spacing.
        binding.messageBody.text =
            Html.fromHtml(message.bodyHtml, Html.FROM_HTML_MODE_COMPACT).trim()

        binding.messageMeta.text =
            "${message.senderName} · ${Format.date(message.publishedAt)}"

        binding.messageUnread.visibility =
            if (message.readAt == null) View.VISIBLE else View.GONE

        if (message.actionRequired && message.dueDate != null) {
            binding.messageAction.visibility = View.VISIBLE
            binding.messageAction.text = binding.root.context.getString(
                R.string.home_form_due,
                Format.plainDate(message.dueDate),
            )
        } else {
            binding.messageAction.visibility = View.GONE
        }

        binding.root.setOnClickListener { viewModel.markRead(message.announcementId) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentListBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        state = StateViews(binding.stateHost, binding.content)
        state.onRetry { viewModel.load() }

        binding.listTitle.text = getString(R.string.nav_messages)
        binding.list.layoutManager = LinearLayoutManager(requireContext())
        binding.list.adapter = adapter
        binding.refresh.setOnRefreshListener { viewModel.load(refreshing = true) }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { current ->
                    binding.refresh.isRefreshing = false
                    state.render(current, adapter::submitList)
                }
            }
        }

        viewModel.load()
    }

    override fun onDestroyView() {
        super.onDestroyView()
        binding.list.adapter = null
        _binding = null
    }
}

/** "ProgrammeUpdate" reads as "PROGRAMME UPDATE" on a card. */
private fun String.spacedUpper(): String =
    replace(Regex("([a-z])([A-Z])"), "$1 $2").uppercase()
