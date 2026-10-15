// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

import android.annotation.SuppressLint
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import androidx.viewbinding.ViewBinding

/**
 * One adapter for every list in the app.
 *
 * [ListAdapter] rather than a plain adapter so a refresh animates the rows
 * that changed instead of redrawing the list — which on a Progress screen that
 * polls means the row under the user's finger stops jumping.
 */
class BindingAdapter<T : Any, B : ViewBinding>(
    private val inflate: (LayoutInflater, ViewGroup, Boolean) -> B,
    private val identity: (T) -> Any,
    private val onBind: (B, T, Int) -> Unit,
) : ListAdapter<T, BindingAdapter.Holder<B>>(diff(identity)) {

    class Holder<B : ViewBinding>(val binding: B) : RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): Holder<B> =
        Holder(inflate(LayoutInflater.from(parent.context), parent, false))

    override fun onBindViewHolder(holder: Holder<B>, position: Int) =
        onBind(holder.binding, getItem(position), position)

    private companion object {
        fun <T : Any> diff(identity: (T) -> Any) = object : DiffUtil.ItemCallback<T>() {
            override fun areItemsTheSame(old: T, new: T) = identity(old) == identity(new)

            // Every row type is a @Serializable data class, so == compares
            // contents; lint cannot see that through the type parameter.
            @SuppressLint("DiffUtilEquals")
            override fun areContentsTheSame(old: T, new: T) = old == new
        }
    }
}
